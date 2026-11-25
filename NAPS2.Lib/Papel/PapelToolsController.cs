using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Notifications;
using NAPS2.Ocr;
using NAPS2.Pdf;
using NAPS2.Scan;

namespace NAPS2.Papel;

/// <summary>
/// Runs the user's PowerShell tools (repdf, smallit, ...) on pages: the pages are exported to a temp PDF, the
/// command runs in the shared console session, and the PDF it writes replaces the pages it ran on.
/// </summary>
public class PapelToolsController
{
    private readonly ScanningContext _scanningContext;
    private readonly Naps2Config _config;
    private readonly UiImageList _imageList;
    private readonly NotificationManager _notificationManager;
    private readonly DesktopFormProvider _desktopFormProvider;
    private readonly DesktopController _desktopController;
    private readonly PowerShellSession _shell;
    private readonly PapelSession _session;

    public PapelToolsController(ScanningContext scanningContext, Naps2Config config, UiImageList imageList,
        NotificationManager notificationManager, DesktopFormProvider desktopFormProvider,
        DesktopController desktopController, PowerShellSession shell, PapelSession session)
    {
        _scanningContext = scanningContext;
        _config = config;
        _imageList = imageList;
        _notificationManager = notificationManager;
        _desktopFormProvider = desktopFormProvider;
        _desktopController = desktopController;
        _shell = shell;
        _session = session;
        _shell.ImportRequested += (_, path) =>
            Invoker.Current.InvokeDispatch(() => _desktopController.ImportFiles([path]));
    }

    public async Task RunTool(PapelTool tool)
    {
        if (_shell.IsBusy)
        {
            Notify("PowerShell is still busy with the previous command");
            return;
        }
        var targets = GetTargets(tool.SelectedOnly);
        if (targets.Count == 0)
        {
            Notify("No pages to run on");
            return;
        }

        _desktopFormProvider.DesktopForm.ShowConsole();
        var progress = new PapelProgress(_notificationManager, $"{tool.Name}: exporting {targets.Count} page(s)…");
        try
        {
            var dir = Path.Combine(Paths.Temp, "papel_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var inPath = Path.Combine(dir, GetBaseName() + ".pdf");
            using (var images = targets.Select(x => x.GetClonedImage()).ToDisposableList())
            {
                var ocrParams = tool.OcrInput ? _config.DefaultOcrParams() : OcrParams.Empty;
                await new PdfExporter(_scanningContext).Export(inPath, images, new PdfExportParams(), ocrParams,
                    progress.Handler);
            }

            string Fill(string s) => s
                .Replace("{in}", inPath)
                .Replace("{dir}", dir)
                .Replace("{name}", Path.GetFileNameWithoutExtension(inPath))
                .Replace("{out}", string.IsNullOrEmpty(tool.Output)
                    ? Path.Combine(dir, "out.pdf")
                    : tool.Output.Replace("{dir}", dir).Replace("{name}", Path.GetFileNameWithoutExtension(inPath)));

            var outPath = string.IsNullOrWhiteSpace(tool.Output) ? null : Fill(tool.Output);
            progress.Update($"{tool.Name}: running…");
            _shell.SetVariable("pdf", inPath);
            await _shell.RunAndWait(Fill(tool.Command));

            if (outPath == null)
            {
                Notify($"{tool.Name} finished");
                return;
            }
            if (!File.Exists(outPath))
            {
                Notify($"{tool.Name} didn't produce {Path.GetFileName(outPath)} - see the console");
                return;
            }
            progress.Update($"{tool.Name}: importing result…");
            await ReplacePages(targets, outPath);
            _shell.SetVariable("out", outPath);
            Notify($"{tool.Name} done - pages replaced (Ctrl+Z to undo)");
        }
        catch (OperationCanceledException)
        {
            Notify($"{tool.Name} was stopped");
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Tool {tool.Name} failed", ex);
            Notify($"{tool.Name} failed: {ex.Message}");
        }
        finally
        {
            progress.Close();
        }
    }

    /// <summary>
    /// Exports the selected pages (or all) to a temp PDF and sets $pdf in the console to its path.
    /// </summary>
    public async Task ExportToVariable()
    {
        var targets = GetTargets(true);
        if (targets.Count == 0) return;
        var dir = Path.Combine(Paths.Temp, "papel_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, GetBaseName() + ".pdf");
        using (var images = targets.Select(x => x.GetClonedImage()).ToDisposableList())
        {
            await new PdfExporter(_scanningContext).Export(path, images, new PdfExportParams(),
                _config.DefaultOcrParams());
        }
        _shell.SetVariable("pdf", path);
    }

    private List<UiImage> GetTargets(bool selectedOnly) =>
        selectedOnly && _imageList.Selection.Any()
            ? _imageList.Images.Where(_imageList.Selection.Contains).ToList()
            : _imageList.Images.ToList();

    private async Task ReplacePages(List<UiImage> targets, string pdfPath)
    {
        var importer = new PdfImporter(_scanningContext);
        var newImages = new List<UiImage>();
        await foreach (var image in importer.Import(pdfPath))
        {
            newImages.Add(new UiImage(image));
        }
        if (newImages.Count == 0) throw new InvalidOperationException("The result PDF has no pages");
        Invoker.Current.Invoke(() =>
            _imageList.Mutate(new ReplacePagesMutation(newImages), ListSelection.From(targets)));
    }

    private string GetBaseName()
    {
        var name = _session.GetFileName()
                   ?? _imageList.Images
                       .Select(x => x.GetImageWeakReference().ProcessedImage.PostProcessingData.OriginalFilePath)
                       .Where(x => x != null)
                       .Select(Path.GetFileNameWithoutExtension)
                       .FirstOrDefault()
                   ?? "pages";
        // Keep it shell- and WSL-friendly
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['\'', '`', '$']))
        {
            name = name!.Replace(c, '_');
        }
        return name!;
    }

    private void Notify(string message) =>
        Invoker.Current.InvokeDispatch(() => _notificationManager.Show(new PapelMessageNotification(message)));
}

/// <summary>
/// Removes the selected pages and inserts the new ones where the first selected page was. Undoable.
/// </summary>
public class ReplacePagesMutation(IReadOnlyList<UiImage> newImages) : ListMutation<UiImage>
{
    public override void Apply(List<UiImage> list, ref ListSelection<UiImage> selection)
    {
        var toRemove = selection.ToHashSet();
        int index = list.FindIndex(toRemove.Contains);
        if (index < 0) index = list.Count;
        int before = list.Take(index).Count(x => !toRemove.Contains(x));
        list.RemoveAll(toRemove.Contains);
        list.InsertRange(before, newImages);
        selection = ListSelection.From(newImages);
    }
}
