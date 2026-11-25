using NAPS2.Papel;
using NAPS2.Pdf;
using Xunit;

namespace NAPS2.Lib.Tests.Papel;

public class TextReflowTests
{
    // A4 in points
    private const double W = 595, H = 842;

    private static PdfTextLine Line(string text, double top, double left = 72, double right = 520, double height = 12) =>
        new(text, left, top, right, top + height);

    [Fact]
    public void JoinsWrappedLinesAndRemovesHyphens()
    {
        var page = new PdfTextPage(W, H, [
            Line("The committee met to discuss the inter-", 100),
            Line("national situation and agreed that the", 114),
            Line("Franco-", 128, right: 120),
        ]);
        var page2 = new PdfTextPage(W, H, [
            Line("Prussian question was settled.", 100, right: 300)
        ]);

        var text = TextReflow.Reflow([page, page2], new TextReflowOptions { StripHeadersFooters = false });

        Assert.Equal(
            "The committee met to discuss the international situation and agreed that the Franco-Prussian question was settled.\n",
            text);
    }

    [Fact]
    public void KeepsParagraphBreaks()
    {
        var page = new PdfTextPage(W, H, [
            Line("First paragraph runs across the full measure of", 100),
            Line("the page and then ends here.", 114, right: 300),
            // Indented first line of the next paragraph
            Line("Second paragraph starts with an indent and also", 128, left: 90),
            Line("wraps onto a second line.", 142, right: 260),
            // Extra space before the third
            Line("Third paragraph after a gap.", 170, right: 280)
        ]);

        var text = TextReflow.Reflow([page], new TextReflowOptions { StripHeadersFooters = false });

        Assert.Equal(
            "First paragraph runs across the full measure of the page and then ends here.\n\n" +
            "Second paragraph starts with an indent and also wraps onto a second line.\n\n" +
            "Third paragraph after a gap.\n",
            text);
    }

    [Fact]
    public void StripsRunningHeadsAndPageNumbers()
    {
        var pages = Enumerable.Range(1, 4).Select(n => new PdfTextPage(W, H, [
            Line(n % 2 == 0 ? $"{n * 10}   A HISTORY OF THINGS" : $"CHAPTER ONE   {n * 10}", 40),
            Line($"Body text on page {n} continues", 100),
            Line("on the next line without a stop", 114),
            Line($"{n * 10}", 800, left: 290, right: 305)
        ])).ToList();

        var text = TextReflow.Reflow(pages);

        Assert.DoesNotContain("HISTORY", text);
        Assert.DoesNotContain("CHAPTER", text);
        Assert.DoesNotContain("40", text);
        Assert.Contains("Body text on page 1 continues on the next line without a stop", text);
    }
}
