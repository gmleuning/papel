using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Notifications;

namespace NAPS2.Papel;

/// <summary>
/// A plain text notification in the corner of the page list. A sticky one stays until closed (used for progress).
/// </summary>
public class PapelMessageNotification : NotificationModel
{
    public PapelMessageNotification(string message, bool sticky = false)
    {
        Message = message;
        Sticky = sticky;
    }

    public string Message { get; private set; }

    public bool Sticky { get; }

    public event EventHandler? MessageChanged;

    public void SetMessage(string message)
    {
        Message = message;
        MessageChanged?.Invoke(this, EventArgs.Empty);
    }

    public override NotificationView CreateView() => new PapelMessageNotificationView(this);
}

public class PapelMessageNotificationView : NotificationView
{
    private readonly PapelMessageNotification _model;
    private readonly Label _label = new();

    public PapelMessageNotificationView(PapelMessageNotification model) : base(model)
    {
        _model = model;
        _label.Text = model.Message;
        HideTimeout = model.Sticky ? 0 : HIDE_SHORT;
        model.MessageChanged += OnMessageChanged;
    }

    private void OnMessageChanged(object? sender, EventArgs e)
    {
        Invoker.Current.InvokeDispatch(() =>
        {
            _label.Text = _model.Message;
            Manager?.InvokeUpdated();
        });
    }

    protected override void BeforeCreateContent()
    {
        _label.BackgroundColor = BackgroundColor;
        _label.Font = new Font(_label.Font.Family, _label.Font.Size, FontStyle.Bold);
    }

    protected override LayoutElement PrimaryContent => _label.DynamicWrap(220).MaxWidth(220).Scale();

    protected override LayoutElement SecondaryContent => C.None();

    public override void Dispose()
    {
        _model.MessageChanged -= OnMessageChanged;
        base.Dispose();
    }
}

/// <summary>
/// Shows a sticky notification with a progress count while a background job runs.
/// </summary>
public sealed class PapelProgress
{
    private readonly NotificationManager _manager;
    private readonly PapelMessageNotification _notification;
    private readonly string _text;

    public PapelProgress(NotificationManager manager, string text)
    {
        _manager = manager;
        _text = text;
        _notification = new PapelMessageNotification(text, sticky: true);
        Invoker.Current.InvokeDispatch(() => _manager.Show(_notification));
    }

    public ProgressHandler Handler =>
        new((current, max) => _notification.SetMessage(max > 0 ? $"{_text}  {current}/{max}" : _text),
            System.Threading.CancellationToken.None);

    public void Update(string text) => _notification.SetMessage(text);

    public void Close() => Invoker.Current.InvokeDispatch(() => _manager.Hide(_notification));
}
