using NAPS2.Images.Bitwise;
using NAPS2.Papel;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.Papel;

public class PapelTransformTests : ContextualTests
{
    [Fact]
    public void AdaptiveThresholdGivesBlackAndWhite()
    {
        using var image = LoadImage(ImageResources.patcht);
        using var result = image.Clone().PerformTransform(new AdaptiveThresholdTransform(15, 61, true, image.Width));

        Assert.Equal(ImagePixelFormat.BW1, result.PixelFormat);
        Assert.Equal(image.Width, result.Width);
        Assert.Equal(image.Height, result.Height);
    }

    [Fact]
    public void AdaptiveThresholdSerializes()
    {
        var serializer = new NAPS2.Serialization.XmlSerializer<List<Transform>>();
        var original = new List<Transform>
        {
            new AdaptiveThresholdTransform(20, 41, false, 2000),
            new MarginWhitenTransform(1, 2, 3, 4, 500, 700)
        };
        var copy = serializer.DeserializeFromXDocument(serializer.SerializeToXDocument(original));
        Assert.Equal(original, copy);
    }

    [Fact]
    public void MarginWhitenPaintsEdgesWhite()
    {
        using var image = LoadImage(ImageResources.dog);
        int w = image.Width, h = image.Height;
        using var result = image.Clone().PerformTransform(new MarginWhitenTransform(20, 20, 20, 20, w, h));
        var path = Path.Combine(FolderPath, "margin.png");
        result.Save(path);
        using var bmp = new System.Drawing.Bitmap(path);

        // A 20px border is white...
        foreach (var (x, y) in new[] { (0, 0), (19, 100), (w - 1, h / 2), (w / 2, h - 1), (w / 2, 10) })
        {
            var px = bmp.GetPixel(x, y);
            Assert.True(px.R == 255 && px.G == 255 && px.B == 255, $"({x},{y}) = {px}");
        }
        // ...and the middle keeps its colour
        var mid = bmp.GetPixel(w / 2, h / 2);
        Assert.False(mid.R == 255 && mid.G == 255 && mid.B == 255);
    }

    [Fact]
    public void FindsGutterOnSyntheticSpread()
    {
        // White page with a dark vertical band at 55% of the width
        using var image = ImageContext.Create(1000, 600, ImagePixelFormat.RGB24);
        new FillColorImageOp(255, 255, 255, 255).Perform(image);
        using (var band = ImageContext.Create(30, 600, ImagePixelFormat.RGB24))
        {
            FillColorImageOp.Black.Perform(band);
            new CopyBitwiseImageOp { DestXOffset = 535 }.Perform(band, image);
        }

        var gutter = PapelImageOps.FindGutter(image);

        Assert.NotNull(gutter);
        Assert.InRange(gutter!.Value, 530, 570);
    }

    [Fact]
    public void ReplacePagesMutationKeepsPosition()
    {
        var a = new UiImage(CreateScannedImage());
        var b = new UiImage(CreateScannedImage());
        var c = new UiImage(CreateScannedImage());
        var n1 = new UiImage(CreateScannedImage());
        var n2 = new UiImage(CreateScannedImage());
        var list = UiImageList.FromImages([a, b, c]);

        list.Mutate(new ReplacePagesMutation([n1, n2]), ListSelection.Of(b));

        Assert.Equal([a, n1, n2, c], list.Images);
    }
}
