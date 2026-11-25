using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using NAPS2.Papel;
using DockStyle = System.Windows.Forms.DockStyle;

namespace NAPS2.WinForms;

/// <summary>
/// The docked PowerShell pane: output on top, a command line below, and a few buttons in a header strip.
/// </summary>
public class PapelConsoleView : UserControl
{
    private static readonly Color Back = Color.FromArgb(0x1e, 0x1e, 0x1e);
    private static readonly Color Fore = Color.FromArgb(0xd4, 0xd4, 0xd4);
    private static readonly Color ErrorColor = Color.FromArgb(0xf4, 0x87, 0x71);
    private static readonly Color CommandColor = Color.FromArgb(0xdc, 0xdc, 0xaa);
    private static readonly Color InfoColor = Color.FromArgb(0x80, 0x80, 0x80);
    private static readonly Color HeaderBack = Color.FromArgb(0x2d, 0x2d, 0x2d);

    private readonly PowerShellSession _shell;
    private readonly RichTextBox _output;
    private readonly TextBox _input;
    private readonly Label _status;
    private readonly ConcurrentQueue<ConsoleLine> _pending = new();
    private readonly System.Windows.Forms.Timer _flushTimer;
    private readonly List<string> _history = [];
    private int _historyIndex;

    public PapelConsoleView(PowerShellSession shell, Func<Task> pagesToVariable, Action hide)
    {
        _shell = shell;
        BackColor = Back;
        var mono = new Font("Cascadia Mono", 9.75f);
        if (mono.Name != "Cascadia Mono") mono = new Font("Consolas", 9.75f);

        _output = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Back,
            ForeColor = Fore,
            Font = mono,
            WordWrap = false,
            DetectUrls = false,
            HideSelection = false
        };

        var prompt = new Label
        {
            Text = "PS>",
            AutoSize = true,
            ForeColor = CommandColor,
            BackColor = Back,
            Font = mono,
            Dock = DockStyle.Left,
            Padding = new Padding(4, 4, 0, 0)
        };
        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Back,
            ForeColor = Fore,
            Font = mono
        };
        _input.KeyDown += InputKeyDown;
        var inputPanel = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Back, Padding = new Padding(0, 5, 4, 0) };
        inputPanel.Controls.Add(_input);
        inputPanel.Controls.Add(prompt);

        _status = new Label
        {
            AutoSize = true,
            ForeColor = Fore,
            BackColor = HeaderBack,
            Dock = DockStyle.Left,
            Padding = new Padding(6, 5, 12, 0),
            Text = "PowerShell"
        };
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = HeaderBack,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 1, 2, 0)
        };
        header.Controls.Add(MakeButton("Hide", hide));
        header.Controls.Add(MakeButton("Clear", () =>
        {
            _output.Clear();
            _shell.ClearHistory();
        }));
        header.Controls.Add(MakeButton("Restart", () => _shell.Restart()));
        header.Controls.Add(MakeButton("Pages → $pdf", () => _ = pagesToVariable()));
        var headerHost = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = HeaderBack };
        headerHost.Controls.Add(header);
        headerHost.Controls.Add(_status);
        header.Dock = DockStyle.Fill;

        Controls.Add(_output);
        Controls.Add(inputPanel);
        Controls.Add(headerHost);

        _flushTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _flushTimer.Tick += (_, _) => Flush();

        foreach (var line in _shell.History) _pending.Enqueue(line);
        _shell.LineReceived += OnLineReceived;
        HandleCreated += (_, _) => _flushTimer.Start();
        HandleDestroyed += (_, _) => _flushTimer.Stop();
        _shell.EnsureStarted();
        _status.Text = _shell.ExecutableName;
    }

    private Button MakeButton(string text, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Fore,
            BackColor = HeaderBack,
            Margin = new Padding(2, 0, 2, 0),
            Padding = new Padding(4, 0, 4, 0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x3e, 0x3e, 0x42);
        button.Click += (_, _) => onClick();
        return button;
    }

    public void FocusInput() => _input.Focus();

    private void OnLineReceived(object? sender, ConsoleLine line) => _pending.Enqueue(line);

    private void Flush()
    {
        if (_pending.IsEmpty) return;
        _output.SuspendLayout();
        while (_pending.TryDequeue(out var line))
        {
            _output.SelectionStart = _output.TextLength;
            _output.SelectionLength = 0;
            _output.SelectionColor = line.Kind switch
            {
                ConsoleLineKind.Error => ErrorColor,
                ConsoleLineKind.Command => CommandColor,
                ConsoleLineKind.Info => InfoColor,
                _ => Fore
            };
            _output.AppendText(line.Text + Environment.NewLine);
        }
        // Keep the box from growing without bound
        if (_output.Lines.Length > 4000)
        {
            _output.Select(0, _output.GetFirstCharIndexFromLine(1000));
            _output.ReadOnly = false;
            _output.SelectedText = "";
            _output.ReadOnly = true;
        }
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
        _output.ResumeLayout();
        _status.Text = _shell.IsBusy ? _shell.ExecutableName + " - running…" : _shell.ExecutableName;
    }

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                var command = _input.Text;
                _input.Clear();
                if (!string.IsNullOrWhiteSpace(command))
                {
                    _history.Remove(command);
                    _history.Add(command);
                }
                _historyIndex = _history.Count;
                _shell.Run(command);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up when _history.Count > 0:
                _historyIndex = Math.Max(0, _historyIndex - 1);
                _input.Text = _history[_historyIndex];
                _input.SelectionStart = _input.TextLength;
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Down when _history.Count > 0:
                _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
                _input.SelectionStart = _input.TextLength;
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                _input.Clear();
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shell.LineReceived -= OnLineReceived;
            _flushTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// A flat toolbar look: no gradients or borders, a soft highlight on hover, following the light/dark colour scheme.
/// </summary>
public class PapelToolStripRenderer : ToolStripProfessionalRenderer
{
    public PapelToolStripRenderer(Color back, Color separator, Color hover, Color pressed)
        : base(new FlatColors(back, separator, hover, pressed))
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // No border
    }

    private class FlatColors(Color back, Color separator, Color hover, Color pressed) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => back;
        public override Color ToolStripGradientMiddle => back;
        public override Color ToolStripGradientEnd => back;
        public override Color ToolStripContentPanelGradientBegin => back;
        public override Color ToolStripContentPanelGradientEnd => back;
        public override Color ToolStripPanelGradientBegin => back;
        public override Color ToolStripPanelGradientEnd => back;
        public override Color ToolStripBorder => back;
        public override Color GripDark => back;
        public override Color GripLight => back;
        public override Color SeparatorDark => separator;
        public override Color SeparatorLight => back;
        public override Color ButtonSelectedHighlight => hover;
        public override Color ButtonSelectedGradientBegin => hover;
        public override Color ButtonSelectedGradientMiddle => hover;
        public override Color ButtonSelectedGradientEnd => hover;
        public override Color ButtonSelectedBorder => hover;
        public override Color ButtonPressedGradientBegin => pressed;
        public override Color ButtonPressedGradientMiddle => pressed;
        public override Color ButtonPressedGradientEnd => pressed;
        public override Color ButtonPressedBorder => pressed;
        public override Color ButtonCheckedGradientBegin => pressed;
        public override Color ButtonCheckedGradientMiddle => pressed;
        public override Color ButtonCheckedGradientEnd => pressed;
        public override Color MenuItemSelected => hover;
        public override Color MenuItemSelectedGradientBegin => hover;
        public override Color MenuItemSelectedGradientEnd => hover;
        public override Color MenuItemPressedGradientBegin => pressed;
        public override Color MenuItemPressedGradientMiddle => pressed;
        public override Color MenuItemPressedGradientEnd => pressed;
        public override Color MenuItemBorder => hover;
        public override Color OverflowButtonGradientBegin => back;
        public override Color OverflowButtonGradientMiddle => back;
        public override Color OverflowButtonGradientEnd => back;
    }
}
