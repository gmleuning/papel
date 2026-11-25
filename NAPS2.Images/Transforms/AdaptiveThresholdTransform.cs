using NAPS2.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local

namespace NAPS2.Images.Transforms;

/// <summary>
/// Converts a page to 1-bit black and white using background flattening followed by an adaptive (local mean)
/// threshold. This is the same pipeline as the "pictopdf"/"repdf" scripts: divide by a heavily blurred copy to remove
/// shading and paper tone, then threshold each pixel against the mean of its neighbourhood minus C.
/// </summary>
[GeneratePropertyAccessors]
public record AdaptiveThresholdTransform : Transform
{
    public AdaptiveThresholdTransform()
    {
    }

    public AdaptiveThresholdTransform(int c, int blockSize, bool flatten, int? originalWidth = null)
    {
        C = c;
        BlockSize = blockSize;
        Flatten = flatten;
        OriginalWidth = originalWidth;
    }

    /// <summary>
    /// Constant subtracted from the local mean. Higher values give a whiter page with thinner strokes.
    /// </summary>
    public int C { get; private set; } = 15;

    /// <summary>
    /// Neighbourhood size in pixels (at the original image width).
    /// </summary>
    public int BlockSize { get; private set; } = 61;

    /// <summary>
    /// Whether to divide out the background (shadows, yellowed paper) before thresholding.
    /// </summary>
    public bool Flatten { get; private set; } = true;

    /// <summary>
    /// The width of the image the block size was chosen for, so the transform scales correctly on thumbnails.
    /// </summary>
    public int? OriginalWidth { get; private set; }
}
