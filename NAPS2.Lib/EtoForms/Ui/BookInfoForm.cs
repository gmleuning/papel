using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.Papel;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Title/author for the current document. Used for the PDF metadata, the default save file name, and the heading
/// of exported clean text.
/// </summary>
public class BookInfoForm : EtoDialogBase
{
    private readonly PapelSession _session;
    private readonly TextBox _title = new();
    private readonly TextBox _author = new();
    private readonly TextBox _template = new();
    private readonly Label _preview = new();

    public BookInfoForm(Naps2Config config, PapelSession session) : base(config)
    {
        _session = session;
        _title.Text = session.Title;
        _author.Text = session.Author;
        _template.Text = PapelSettings.Current.FileNameTemplate;
        _title.TextChanged += (_, _) => UpdatePreview();
        _author.TextChanged += (_, _) => UpdatePreview();
        _template.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    protected override void BuildLayout()
    {
        Title = "Book info";
        FormStateController.RestoreFormState = false;
        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            C.Label("Title"),
            _title,
            C.Label("Author"),
            _author,
            C.Label("File name template  -  $(author), $(title)"),
            _template,
            _preview,
            L.Row(
                C.Link("Clear", () =>
                {
                    _title.Text = "";
                    _author.Text = "";
                }),
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Submit),
                    C.CancelButton(this))
            )
        );
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _title.Focus();
    }

    private void UpdatePreview()
    {
        var probe = new PapelSession { Title = _title.Text ?? "", Author = _author.Text ?? "" };
        var saved = PapelSettings.Current.FileNameTemplate;
        PapelSettings.Current.FileNameTemplate = _template.Text ?? "";
        var name = probe.GetFileName();
        PapelSettings.Current.FileNameTemplate = saved;
        _preview.Text = name != null ? $"Saves as: {name}.pdf" : "Saves as: (the usual default)";
    }

    private void Submit()
    {
        _session.Title = _title.Text?.Trim() ?? "";
        _session.Author = _author.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(_template.Text))
        {
            PapelSettings.Current.FileNameTemplate = _template.Text.Trim();
            PapelSettings.Current.Save();
        }
    }
}
