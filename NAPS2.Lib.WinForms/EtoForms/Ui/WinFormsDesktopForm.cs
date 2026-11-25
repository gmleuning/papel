using System.ComponentModel;
using System.Drawing;
using System.Linq.Expressions;
using Eto.Forms;
using Eto.WinForms;
using Eto.WinForms.Forms.ToolBar;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Notifications;
using NAPS2.EtoForms.Widgets;
using NAPS2.EtoForms.WinForms;
using NAPS2.Papel;
using NAPS2.Scan;
using NAPS2.WinForms;
using WF = System.Windows.Forms;

namespace NAPS2.EtoForms.Ui;

public class WinFormsDesktopForm : DesktopForm
{
    private readonly Dictionary<DesktopToolbarMenuType, WF.ToolStripSplitButton> _menuButtons = new();
    private readonly ToolbarFormatter _toolbarFormatter = new(new StringWrapper());
    private readonly WF.Form _form;
    private WF.ToolStrip _mainToolStrip = null!;
    private WF.ToolStrip _profilesToolStrip = null!;
    private WF.ToolStripContainer _container = null!;
    private readonly PapelServices _papel;
    private WF.SplitContainer _consoleSplit = null!;
    private PapelConsoleView? _consoleView;

    public WinFormsDesktopForm(
        Naps2Config config,
        DesktopKeyboardShortcuts keyboardShortcuts,
        NotificationManager notificationManager,
        CultureHelper cultureHelper,
        ColorScheme colorScheme,
        IProfileManager profileManager,
        UiImageList imageList,
        ThumbnailController thumbnailController,
        UiThumbnailProvider thumbnailProvider,
        DesktopController desktopController,
        IDesktopScanController desktopScanController,
        ImageListActions imageListActions,
        ImageListViewBehavior imageListViewBehavior,
        DesktopFormProvider desktopFormProvider,
        IDesktopSubFormController desktopSubFormController,
        Lazy<DesktopCommands> commands,
        Sidebar sidebar,
        IIconProvider iconProvider,
        EditWithController editWithController,
        PapelServices papel)
        : base(config, keyboardShortcuts, notificationManager, cultureHelper, colorScheme, profileManager, imageList,
            thumbnailController, thumbnailProvider, desktopController, desktopScanController, imageListActions,
            imageListViewBehavior, desktopFormProvider, desktopSubFormController, commands, sidebar, iconProvider,
            editWithController)
    {
        _papel = papel;
        _form = this.ToNative();
        _form.FormClosing += OnFormClosing;

        // TODO: Remove this if https://github.com/picoe/Eto/issues/2601 is fixed
        NativeListView.KeyDown += (_, e) => OnKeyDown(new KeyEventArgs(e.KeyData.ToEto(), KeyEventType.KeyDown));

        Load += (_, _) => colorScheme.ColorSchemeChanged += ColorSchemeChanged;
        UnLoad += (_, _) => colorScheme.ColorSchemeChanged -= ColorSchemeChanged;
    }

    private void ColorSchemeChanged(object? sender, EventArgs e)
    {
        Invoker.Current.Invoke(WinFormsHacks.ClearCachedBrushesAndPens);
        Invoker.Current.InvokeDispatch(() =>
        {
            if (WF.Application.OpenForms.Count == 1)
            {
                // Reload the form as WinForms dark mode doesn't dynamically switch everything
                SetCulture(Config.Get(c => c.Culture) ?? "en");
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Don't do anything here as we have a separate FormClosing event handler
        // That allows us to check the close reason (which Eto doesn't provide)
    }

    internal WF.ToolStrip MainToolStrip => _mainToolStrip;

    private void OnFormClosing(object? sender, WF.FormClosingEventArgs e)
    {
        if (!_desktopController.PrepareForClosing(e.CloseReason == WF.CloseReason.UserClosing))
        {
            e.Cancel = true;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        NativeListView.TabIndex = 7;
        NativeListView.BorderStyle = WF.BorderStyle.None;
    }

    protected override LayoutElement GetControlButtons()
    {
        // Disabled buttons don't prevent click events from being sent to the listview below the button, so without this
        // "mouse catcher" control you could e.g. spam click zoom out until it's maxed and then accidentally keep
        // clicking and change the listview selection.
        var mouseCatcher = new WF.Button
        {
            BackColor = Color.White,
            Size = new Size(45, 23),
            FlatStyle = WF.FlatStyle.Flat
        };
        return L.Row(
            GetSidebarButton(),
            L.Overlay(
                L.Row(mouseCatcher.ToEto().AlignTrailing()),
                GetZoomButtons()
            )
        );
    }

    private WF.ListView NativeListView => ((WinFormsListView<UiImage>) _listView).NativeControl;

    protected override void CreateToolbarsAndMenus()
    {
        if (ToolBar == null)
        {
            ToolBar = new ToolBar();
            ConfigureToolbars();
        }
        _mainToolStrip.SuspendLayout();
        base.CreateToolbarsAndMenus();
        _mainToolStrip.ResumeLayout();
    }

    protected override void RecreateToolbarsAndMenus()
    {
        base.RecreateToolbarsAndMenus();
        _toolbarFormatter.RelayoutToolbar(_mainToolStrip, EtoPlatform.Current.GetScaleFactor(this));
    }

    protected override void ConfigureToolbars()
    {
        _container = new WF.ToolStripContainer();
        _mainToolStrip = ((ToolBarHandler) ToolBar.Handler).Control;
        _profilesToolStrip = new WF.ToolStrip();

        _mainToolStrip.ShowItemToolTips = false;
        _mainToolStrip.TabStop = true;
        _mainToolStrip.GripStyle = WF.ToolStripGripStyle.Hidden;
        _mainToolStrip.Padding = new WF.Padding(6, 2, 6, 2);
        var back = _colorScheme.BackgroundColor.ToSD();
        var hover = _colorScheme.HighlightBackgroundColor.ToSD();
        var pressed = _colorScheme.HighlightBorderColor.ToSD();
        _mainToolStrip.Renderer = new PapelToolStripRenderer(back, _colorScheme.SeparatorColor.ToSD(), hover,
            Color.FromArgb(90, pressed));
        _mainToolStrip.BackColor = back;
        foreach (var panel in _container.Controls.OfType<WF.ToolStripPanel>())
        {
            panel.BackColor = back;
        }
        EtoPlatform.Current.AttachDpiDependency(this,
            scale => _mainToolStrip.ImageScalingSize = new Size((int) (32 * scale), (int) (32 * scale)));

        _profilesToolStrip.ShowItemToolTips = false;
        _profilesToolStrip.TabStop = true;
        _profilesToolStrip.Location = new Point(0, 1000);
        EtoPlatform.Current.AttachDpiDependency(this,
            scale => _profilesToolStrip.ImageScalingSize = new Size((int) (16 * scale), (int) (16 * scale)));

        var contextMenu = (WF.ContextMenuStrip) C.ContextMenu(this, GetToolbarContextMenuProvider()).ControlObject;
        _mainToolStrip.ContextMenuStrip = contextMenu;
        _profilesToolStrip.ContextMenuStrip = contextMenu;
        foreach (var panel in _container.Controls.OfType<WF.ToolStripPanel>())
        {
            // Allow tabbing through the toolbar for accessibility
            WinFormsHacks.SetControlStyle(panel, WF.ControlStyles.Selectable, true);
            panel.ContextMenuStrip = contextMenu;
        }

        // We defer this to the Load event because otherwise with RTL languages we get a very weird crash (.NET bug not
        // preset with .NET framework). Ideally I could file an issue but it's very hard to get a minimum reproducible
        // test case.
        // See https://github.com/cyanfish/naps2/issues/586
        Load += (_, _) =>
        {
            _container.TopToolStripPanel.Controls.Add(_mainToolStrip);
            _mainToolStrip.ParentChanged += (_, _) =>
            {
                _toolbarFormatter.RelayoutToolbar(_mainToolStrip, EtoPlatform.Current.GetScaleFactor(this));
                LayoutController.Invalidate();
                SyncToolStripLocation(_mainToolStrip, c => c.DesktopToolStripDock);
            };
            PlaceToolbars();
            _profilesToolStrip.ParentChanged += (_, _) =>
            {
                LayoutController.Invalidate();
                SyncToolStripLocation(_profilesToolStrip, c => c.ProfilesToolStripDock);
            };
        };
    }

    private void SyncToolStripLocation(WF.ToolStrip toolStrip, Expression<Func<CommonConfig, DockStyle>> accessor)
    {
        // Save the position if the user drags the toolbar to a different dock
        if (toolStrip.Parent?.Dock.ToConfig() is { } dock && dock != ToolbarConfig.Get(accessor))
        {
            Config.User.Set(accessor, dock);
        }
    }

    public override void PlaceToolbars()
    {
        // Papel: no scan profiles toolbar
        if (_profilesToolStrip.Parent != null)
        {
            _profilesToolStrip.Parent.Controls.Remove(_profilesToolStrip);
        }
        SetDock(_mainToolStrip, ToolbarConfig.Get(c => c.DesktopToolStripDock));
        if (_profilesToolStrip.Parent != null)
        {
            SetDock(_profilesToolStrip, ToolbarConfig.Get(c => c.ProfilesToolStripDock));
        }
    }

    protected override void UpdateProfilesToolbar()
    {
        // Papel: no scan profiles
    }

    protected override void BuildLayout()
    {
        base.BuildLayout();

        var wfContent = LayoutController.Container.ToNative();
        wfContent.Dock = WF.DockStyle.Fill;
        var etoContainer = _container.ToEto();
        Content = etoContainer;

        _consoleSplit = new WF.SplitContainer
        {
            Dock = WF.DockStyle.Fill,
            Orientation = WF.Orientation.Horizontal,
            FixedPanel = WF.FixedPanel.Panel2,
            SplitterWidth = 5,
            Panel2Collapsed = true,
            BackColor = _colorScheme.SeparatorColor.ToSD()
        };
        _consoleSplit.Panel1.BackColor = _colorScheme.BackgroundColor.ToSD();
        _consoleSplit.Panel1.Controls.Add(wfContent);
        _consoleSplit.SplitterMoved += (_, _) =>
        {
            if (_consoleSplit.Panel2Collapsed) return;
            PapelSettings.Current.ConsoleHeight = _consoleSplit.Height - _consoleSplit.SplitterDistance;
        };
        _container.ContentPanel.Controls.Add(_consoleSplit);

        DrawContentBorders(wfContent);
    }

    protected override void OnShown(EventArgs e)
    {
        EtoPlatform.Current.AttachDpiDependency(this,
            scale => _toolbarFormatter.RelayoutToolbar(_mainToolStrip, scale));
        NativeListView.Focus();
        base.OnShown(e);
        if (PapelSettings.Current.ConsoleVisible)
        {
            SetConsoleVisible(true, focus: false);
        }
    }

    public override void ToggleConsole() => SetConsoleVisible(_consoleSplit.Panel2Collapsed, focus: true);

    public override void ShowConsole()
    {
        if (_consoleSplit.Panel2Collapsed) SetConsoleVisible(true, focus: false);
    }

    private void SetConsoleVisible(bool visible, bool focus)
    {
        if (visible)
        {
            if (_consoleView == null)
            {
                _consoleView = new PapelConsoleView(_papel.Shell, _papel.Tools.ExportToVariable,
                    () => SetConsoleVisible(false, false))
                {
                    Dock = WF.DockStyle.Fill
                };
                _consoleSplit.Panel2.Controls.Add(_consoleView);
            }
            _consoleSplit.Panel2Collapsed = false;
            var height = Math.Clamp(PapelSettings.Current.ConsoleHeight, 80,
                Math.Max(80, _consoleSplit.Height - 120));
            _consoleSplit.SplitterDistance = Math.Max(0, _consoleSplit.Height - height - _consoleSplit.SplitterWidth);
            if (focus) _consoleView.FocusInput();
        }
        else
        {
            _consoleSplit.Panel2Collapsed = true;
            NativeListView.Focus();
        }
        if (PapelSettings.Current.ConsoleVisible != visible)
        {
            PapelSettings.Current.ConsoleVisible = visible;
            PapelSettings.Current.Save();
        }
    }

    private void DrawContentBorders(WF.Control content)
    {
        // A single hairline between the toolbar and the page list
        var pen = new Pen(_colorScheme.SeparatorColor.ToSD());
        content.Paint += (_, args) => args.Graphics.DrawLine(pen, 0, 0, content.Width, 0);
    }

    protected override void SetThumbnailSpacing(int thumbnailSize, float scale)
    {
        const int MIN_PADDING = 6;
        const int MAX_PADDING = 66;
        // Linearly scale the padding with the thumbnail size
        int padding = MIN_PADDING + (MAX_PADDING - MIN_PADDING) * (thumbnailSize - ThumbnailSizes.MIN_SIZE) /
            (ThumbnailSizes.MAX_SIZE - ThumbnailSizes.MIN_SIZE);
        int hSpacing = thumbnailSize + padding;
        int vSpacing = thumbnailSize + padding * 2;
        WinFormsHacks.SetListSpacing(NativeListView,
            (int) Math.Round(hSpacing * scale),
            (int) Math.Round(vSpacing * scale));
    }

    protected override void CreateToolbarButton(Command command)
    {
        var item = new WF.ToolStripButton
        {
            TextImageRelation = WF.TextImageRelation.ImageAboveText
        };
        ApplyCommand(item, command);
        _mainToolStrip.Items.Add(item);
    }

    protected override void CreateToolbarButtonWithMenu(Command command, DesktopToolbarMenuType menuType,
        MenuProvider menu)
    {
        var item = new WF.ToolStripSplitButton
        {
            TextImageRelation = WF.TextImageRelation.ImageAboveText
        };
        EtoPlatform.Current.AttachDpiDependency(this, scale => item.DropDownButtonWidth = (int) (scale * 15));
        ApplyCommand(item, command);
        _mainToolStrip.Items.Add(item);
        menu.Handle(subItems => SetUpMenu(item, subItems));
        _menuButtons[menuType] = item;
    }

    private void SetUpMenu(WF.ToolStripDropDownItem item, List<MenuProvider.Item> subItems)
    {
        item.DropDown.SuspendLayout();
        item.DropDownItems.Clear();
        foreach (var subItem in subItems)
        {
            switch (subItem)
            {
                case MenuProvider.SeparatorItem:
                    item.DropDownItems.Add(new WF.ToolStripSeparator());
                    break;
                case MenuProvider.CommandItem commandSubItem:
                    item.DropDownItems.Add(ApplyCommand(new WF.ToolStripMenuItem
                    {
                        ImageScaling = WF.ToolStripItemImageScaling.None
                    }, commandSubItem.Command));
                    break;
                case MenuProvider.SubMenuItem subMenuSubItem:
                    var subMenu = new WF.ToolStripMenuItem();
                    ApplyCommand(subMenu, subMenuSubItem.Command);
                    // TODO: If submenus are dynamic this will memory leak or something
                    subMenuSubItem.MenuProvider.Handle(subSubItems => SetUpMenu(subMenu, subSubItems));
                    item.DropDownItems.Add(subMenu);
                    break;
            }
        }
        // Only relayout if the menu is already visible
        item.DropDown.ResumeLayout(item.DropDown.IsHandleCreated);
    }

    protected override void CreateToolbarMenu(Command command, MenuProvider menu)
    {
        var item = new WF.ToolStripDropDownButton
        {
            TextImageRelation = WF.TextImageRelation.ImageAboveText,
            ShowDropDownArrow = false
        };
        ApplyCommand(item, command);
        _mainToolStrip.Items.Add(item);
        menu.Handle(subItems => SetUpMenu(item, subItems));
    }

    protected override void CreateToolbarStackedButtons(Command command1, Command command2)
    {
        var item = new ToolStripDoubleButton
        {
            FirstText = command1.ToolBarText,
            SecondText = command2.ToolBarText
        };
        item.AccessibleName = string.Join(" ", command1.ToolBarText, command2.ToolBarText);
        EtoPlatform.Current.AttachDpiDependency(this, scale =>
        {
            item.FirstImage = ((ActionCommand) command1).GetIconImage(IconScenario.SmallToolbar, scale).ToSD();
            item.SecondImage = ((ActionCommand) command2).GetIconImage(IconScenario.SmallToolbar, scale).ToSD();
        });
        command1.EnabledChanged += (_, _) => item.Enabled = command1.Enabled;
        item.FirstClick += (_, _) => command1.Execute();
        item.SecondClick += (_, _) => command2.Execute();
        _mainToolStrip.Items.Add(item);
    }

    private WF.ToolStripItem ApplyCommand(WF.ToolStripItem item, Command command)
    {
        void SetItemText() => item.Text = item is WF.ToolStripMenuItem ? command.MenuText : command.ToolBarText;
        EtoPlatform.Current.AttachDpiDependency(this,
            scale => item.Image = ((ActionCommand) command).GetIconImage(
                    item is WF.ToolStripMenuItem ? IconScenario.Menu : IconScenario.Toolbar, scale)
                .ToSD());
        SetItemText();
        if (command is ActionCommand actionCommand)
        {
            actionCommand.TextChanged += (_, _) => SetItemText();
        }
        // TODO: We want a better way of determining which keyboard shortcuts are worth showing
        // Ideally we could show them all, but it can be really distracting. So only showing F2/F3 etc. right now.
        if (item is WF.ToolStripMenuItem menuItem && !command.Shortcut.ToString().Contains(","))
        {
            var swfKeys = command.Shortcut.ToSWF();
            if (WF.ToolStripManager.IsValidShortcut(swfKeys))
            {
                menuItem.ShortcutKeys = swfKeys;
            }
        }
        command.EnabledChanged += (_, _) => item.Enabled = command.Enabled;
        if (item is WF.ToolStripSplitButton button)
        {
            button.ButtonClick += (_, _) => command.Execute();
        }
        else
        {
            item.Click += (_, _) => command.Execute();
        }
        return item;
    }

    protected override void CreateToolbarSeparator()
    {
        _mainToolStrip.Items.Add(new WF.ToolStripSeparator());
    }

    public override void ShowToolbarMenu(DesktopToolbarMenuType menuType)
    {
        _menuButtons.Get(menuType)?.ShowDropDown();
    }

    private void SetDock(WF.ToolStrip toolStrip, DockStyle dock)
    {
        var wfDock = dock.ToWinForms();
        var panel = _container.Controls.OfType<WF.ToolStripPanel>().FirstOrDefault(x => x.Dock == wfDock);
        if (panel != null)
        {
            toolStrip.Parent = panel;
        }
        toolStrip.Parent!.TabStop = true;
    }
}