using NAPS2.EtoForms;
using NAPS2.EtoForms.Ui;

namespace NAPS2.Papel;

/// <summary>
/// Bundles the Papel controllers so the desktop commands take one dependency instead of six.
/// </summary>
public class PapelServices
{
    private readonly IFormFactory _formFactory;

    public PapelServices(CleanTextController cleanText, PapelToolsController tools, SpreadSplitter spreadSplitter,
        PowerShellSession shell, PapelSession session, IFormFactory formFactory)
    {
        CleanText = cleanText;
        Tools = tools;
        SpreadSplitter = spreadSplitter;
        Shell = shell;
        Session = session;
        _formFactory = formFactory;
    }

    public CleanTextController CleanText { get; }
    public PapelToolsController Tools { get; }
    public SpreadSplitter SpreadSplitter { get; }
    public PowerShellSession Shell { get; }
    public PapelSession Session { get; }

    public void ShowBookInfo()
    {
        var form = _formFactory.Create<BookInfoForm>();
        form.ShowModal();
    }
}
