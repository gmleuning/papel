using System.Collections.Immutable;

namespace NAPS2.Ocr;

/// <summary>
/// A element in the result of an OCR request that represents a text segment.
/// </summary>
public record OcrResultElement(
    string Text,
    string LanguageCode,
    bool RightToLeft,
    (int x, int y, int w, int h) Bounds,
    int Baseline,
    int FontSize,
    ImmutableList<OcrResultElement> Children)
{
    /// <summary>
    /// The index of the paragraph (hOCR ocr_par) this line belongs to, or -1 if unknown. Lines with the same id are
    /// wrapped lines of one paragraph.
    /// </summary>
    public int ParagraphId { get; init; } = -1;
}