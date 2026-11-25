using NAPS2.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local

namespace NAPS2.Images.Transforms;

/// <summary>
/// Paints the outer margins of a page white without changing its size. Used to remove scanner edges, gutter shadows
/// and finger marks. Margins are in pixels relative to OriginalWidth/OriginalHeight.
/// </summary>
[GeneratePropertyAccessors]
public record MarginWhitenTransform : Transform
{
    public MarginWhitenTransform()
    {
    }

    public MarginWhitenTransform(int left, int right, int top, int bottom, int? originalWidth = null,
        int? originalHeight = null)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
    }

    public int Left { get; private set; }
    public int Right { get; private set; }
    public int Top { get; private set; }
    public int Bottom { get; private set; }

    public int? OriginalWidth { get; private set; }
    public int? OriginalHeight { get; private set; }

    public override bool IsNull => Left <= 0 && Right <= 0 && Top <= 0 && Bottom <= 0;
}
