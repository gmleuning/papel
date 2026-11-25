using Eto.Forms;
using NAPS2.EtoForms;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Notifications;
using NAPS2.Ocr;
using NAPS2.Pdf;
using NAPS2.Scan;

namespace NAPS2.Papel;

/// <summary>
/// "Clean text": gets the text of the pages as paragraphs instead of one hard line per printed line.
/// The pages are written to a temporary PDF exactly as Save PDF would (keeping any existing text layer and OCRing
/// pages that have none), the text layer is read back with positions, and the lines are reflowed.
/// </summary>
public class CleanTextController
{
    private readonly ScanningContext _scanningContext;
    private readonly Naps2Config _config;
    private readonly UiImageList _imageList;
    private readonly NotificationManager _notificationManager;
    private readonly PapelSession _session;
    private bool _busy;

    public CleanTextController(ScanningContext scanningContext, Naps2Config config, UiImageList imageList,
        NotificationManager notificationManager, PapelSession session)
    {
        _scanningContext = scanningContext;
        _config = config;
        _imageList = imageList;
        _notificationManager = notificationManager;
        _session = session;
    }

    public async Task CopyToClipboard(bool selectedOnly)
    {
        var text = await GetCleanText(selectedOnly);
        if (text == null) return;
        Invoker.Current.Invoke(() => Clipboard.Instance.Text = text);
        Notify($"Copied clean text ({CountWords(text):N0} words)");
    }

    public async Task SaveAs(bool selectedOnly)
    {
        string? path = null;
        Invoker.Current.Invoke(() =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export clean text",
                FileName = (_session.GetFileName() ?? GetSourceName() ?? "text") + ".md",
                Filters =
                {
                    new FileFilter("Markdown", ".md"),
                    new FileFilter("Plain text", ".txt")
                }
            };
            var dir = GetSourceDirectory();
            if (dir != null) dialog.Directory = new Uri(dir);
            if (dialog.ShowDialog(null) == DialogResult.Ok)
            {
                path = dialog.FileName;
            }
        });
        if (path == null) return;

        var text = await GetCleanText(selectedOnly, markdown: Path.GetExtension(path).ToLowerInvariant() != ".txt");
        if (text == null) return;
        if (!string.IsNullOrWhiteSpace(_session.Title) && Path.GetExtension(path).ToLowerInvariant() == ".md")
        {
            var header = $"# {_session.Title.Trim()}\n";
            if (!string.IsNullOrWhiteSpace(_session.Author)) header += $"\n*{_session.Author.Trim()}*\n";
            text = header + "\n" + text;
        }
        // UTF-8 without BOM: what ripgrep, pandoc and most editors expect
        await File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(false));
        Invoker.Current.InvokeDispatch(() =>
            _notificationManager.Show(new SaveNotification("Clean text saved", path)));
    }

    /// <summary>
    /// Returns the reflowed text of the pages, or null if there are no pages or no text was found.
    /// </summary>
    public async Task<string?> GetCleanText(bool selectedOnly, bool markdown = true)
    {
        if (_busy)
        {
            Notify("Clean text is already running");
            return null;
        }
        var uiImages = selectedOnly && _imageList.Selection.Any()
            ? _imageList.Images.Where(_imageList.Selection.Contains).ToList()
            : _imageList.Images.ToList();
        if (uiImages.Count == 0) return null;

        _busy = true;
        var progress = new PapelProgress(_notificationManager, $"Reading text from {uiImages.Count} page(s)…");
        var tempPdf = Path.Combine(Paths.Temp, $"papel_text_{Guid.NewGuid():N}.pdf");
        try
        {
            using var images = uiImages.Select(x => x.GetClonedImage()).ToDisposableList();
            var exporter = new PdfExporter(_scanningContext);
            await exporter.Export(tempPdf, images, new PdfExportParams(), GetOcrParams(), progress.Handler);
            var pages = await Task.Run(() => PdfTextLayout.ReadLines(tempPdf));
            if (pages.All(p => p.Lines.Count == 0))
            {
                Notify("No text found. Turn on OCR (or install an OCR language) and try again.");
                return null;
            }
            var text = TextReflow.Reflow(pages, new TextReflowOptions
            {
                StripHeadersFooters = PapelSettings.Current.StripHeadersFooters,
                MarkdownHeadings = markdown
            });
            return text;
        }
        catch (Exception ex)
        {
            Log.ErrorException("Clean text failed", ex);
            Notify("Clean text failed: " + ex.Message);
            return null;
        }
        finally
        {
            _busy = false;
            progress.Close();
            try
            {
                File.Delete(tempPdf);
            }
            catch (Exception)
            {
                // Ignore
            }
        }
    }

    private OcrParams GetOcrParams()
    {
        var ocrParams = _config.DefaultOcrParams();
        if (ocrParams.LanguageCode != null) return ocrParams;
        // OCR is turned off for saving, but clean text needs it for pages without a text layer
        var lang = _config.Get(c => c.OcrLanguageCode);
        return string.IsNullOrEmpty(lang)
            ? ocrParams
            : new OcrParams(lang, OcrMode.Default, _config.Get(c => c.OcrTimeoutInSeconds));
    }

    private string? GetSourcePath() =>
        _imageList.Images
            .Select(x => x.GetImageWeakReference().ProcessedImage.PostProcessingData.OriginalFilePath)
            .FirstOrDefault(x => x != null);

    private string? GetSourceName() =>
        GetSourcePath() is { } p ? Path.GetFileNameWithoutExtension(p) : null;

    private string? GetSourceDirectory() =>
        GetSourcePath() is { } p ? Path.GetDirectoryName(p) : null;

    private static int CountWords(string text) =>
        text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries).Length;

    private void Notify(string message) =>
        Invoker.Current.InvokeDispatch(() => _notificationManager.Show(new PapelMessageNotification(message)));
}
