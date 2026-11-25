using NAPS2.EtoForms.Notifications;
using NAPS2.Images.Bitwise;

namespace NAPS2.Papel;

/// <summary>
/// Splits two-page book spreads into single pages at the detected gutter.
/// </summary>
public class SpreadSplitter
{
    private readonly UiImageList _imageList;
    private readonly NotificationManager _notificationManager;

    public SpreadSplitter(UiImageList imageList, NotificationManager notificationManager)
    {
        _imageList = imageList;
        _notificationManager = notificationManager;
    }

    public async Task SplitSelected()
    {
        var targets = _imageList.Images.Where(_imageList.Selection.Contains).ToList();
        if (targets.Count == 0) targets = _imageList.Images.ToList();
        if (targets.Count == 0) return;

        var progress = new PapelProgress(_notificationManager, $"Splitting {targets.Count} spread(s)…");
        try
        {
            var results = await Task.Run(() =>
            {
                var list = new List<(UiImage original, UiImage left, UiImage right, bool centred)>();
                foreach (var target in targets)
                {
                    using var processed = target.GetClonedImage();
                    using var rendered = processed.Render();
                    // Portrait pages aren't spreads; leave them alone
                    if (rendered.Width < rendered.Height * 0.9) continue;
                    var gutter = PapelImageOps.FindGutter(rendered);
                    int w = rendered.Width, h = rendered.Height;
                    int x = gutter ?? w / 2;
                    var leftCrop = new CropTransform(0, w - x, 0, 0, w, h);
                    var rightCrop = new CropTransform(x, 0, 0, 0, w, h);
                    var left = new UiImage(target.GetClonedImage(), useThumbnail: false);
                    var right = new UiImage(target.GetClonedImage(), useThumbnail: false);
                    left.AddTransform(leftCrop);
                    right.AddTransform(rightCrop);
                    list.Add((target, left, right, gutter == null));
                }
                return list;
            });

            if (results.Count == 0)
            {
                Notify("No landscape spreads in the selection");
                return;
            }
            Invoker.Current.Invoke(() =>
            {
                foreach (var (original, left, right, _) in results)
                {
                    _imageList.Mutate(new ReplacePagesMutation([left, right]), ListSelection.Of(original));
                }
                _imageList.UpdateSelection(ListSelection.From(results.SelectMany(r => new[] { r.left, r.right })));
            });
            int centred = results.Count(r => r.centred);
            Notify(centred == 0
                ? $"Split {results.Count} spread(s)"
                : $"Split {results.Count} spread(s); {centred} had no clear gutter and were cut in the middle");
        }
        catch (Exception ex)
        {
            Log.ErrorException("Split spreads failed", ex);
            Notify("Split spreads failed: " + ex.Message);
        }
        finally
        {
            progress.Close();
        }
    }

    private void Notify(string message) =>
        Invoker.Current.InvokeDispatch(() => _notificationManager.Show(new PapelMessageNotification(message)));
}
