using System.Text;
using System.Text.RegularExpressions;
using NAPS2.Pdf;

namespace NAPS2.Papel;

public record TextReflowOptions
{
    /// <summary>Drop running headers, footers and page numbers.</summary>
    public bool StripHeadersFooters { get; init; } = true;

    /// <summary>Mark lines in a noticeably larger font as Markdown headings.</summary>
    public bool MarkdownHeadings { get; init; } = true;

    /// <summary>Join a paragraph that runs over a page break into one.</summary>
    public bool JoinAcrossPages { get; init; } = true;
}

/// <summary>
/// Turns the line-per-line text layer of a scanned (OCR'd) PDF into clean paragraphs: wrapped lines are joined,
/// words hyphenated across a line break are rejoined, and real paragraph breaks are kept. Paragraph breaks are
/// detected from geometry (extra vertical space, first-line indent, a short line ending a sentence).
/// </summary>
public static partial class TextReflow
{
    // Sentence ends. ':' and ';' are deliberately not here: they rarely end a paragraph.
    private const string TerminalPunctuation = ".!?\"”’»)]";

    public static string Reflow(IReadOnlyList<PdfTextPage> pages, TextReflowOptions? options = null)
    {
        options ??= new TextReflowOptions();
        var pageLines = pages.Select(p => p.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList()).ToList();
        if (options.StripHeadersFooters)
        {
            StripHeadersFooters(pages, pageLines);
        }

        double bodyHeight = Median(pageLines.SelectMany(x => x).Select(l => l.Height).Where(h => h > 0)) ?? 10;

        var paragraphs = new List<Paragraph>();
        for (int p = 0; p < pageLines.Count; p++)
        {
            var pageParas = SplitIntoParagraphs(pageLines[p], bodyHeight);
            if (pageParas.Count == 0) continue;
            if (options.JoinAcrossPages && paragraphs.Count > 0 && ContinuesOnNextPage(paragraphs[^1], pageParas[0]))
            {
                paragraphs[^1].Lines.AddRange(pageParas[0].Lines);
                pageParas.RemoveAt(0);
            }
            paragraphs.AddRange(pageParas);
        }

        var sb = new StringBuilder();
        foreach (var para in paragraphs)
        {
            var text = JoinLines(para.Lines.Select(l => l.Text));
            if (text.Length == 0) continue;
            // OCR line heights vary a lot with ascenders/descenders, so only a clearly bigger line that doesn't
            // start mid-sentence counts as a heading
            if (options.MarkdownHeadings && para.Lines.Count <= 2 && para.Height > bodyHeight * 1.6 &&
                text.Length < 120 && !char.IsLower(text[0]) && !text.TrimEnd().EndsWith('.') &&
                !text.TrimEnd().EndsWith(';') && !text.TrimEnd().EndsWith(','))
            {
                text = "## " + text;
            }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(text);
        }
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Joins wrapped lines into one string, rejoining words hyphenated at line ends.
    /// </summary>
    public static string JoinLines(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = WhitespaceRegex().Replace(raw.Trim(), " ");
            if (line.Length == 0) continue;
            if (sb.Length == 0)
            {
                sb.Append(line);
                continue;
            }
            char last = sb[^1];
            char beforeLast = sb.Length > 1 ? sb[^2] : ' ';
            bool hyphenated = (last is '-' or '­' or '¬' or '‐') && char.IsLetter(beforeLast);
            if (hyphenated && char.IsLower(line[0]))
            {
                // "inter- national" -> "international"
                sb.Length--;
                sb.Append(line);
            }
            else if (hyphenated)
            {
                // "Franco- Prussian" -> "Franco-Prussian" (keep the hyphen, drop the break)
                if (last != '-') sb[^1] = '-';
                sb.Append(line);
            }
            else
            {
                sb.Append(' ').Append(line);
            }
        }
        return sb.ToString().Replace("­", "");
    }

    private class Paragraph
    {
        public List<PdfTextLine> Lines { get; } = [];
        public double Height => Lines.Count == 0 ? 0 : Lines.Average(l => l.Height);
    }

    private static List<Paragraph> SplitIntoParagraphs(List<PdfTextLine> lines, double bodyHeight)
    {
        var result = new List<Paragraph>();
        if (lines.Count == 0) return result;

        // Typical distance from one line's top to the next within a paragraph
        var deltas = new List<double>();
        for (int i = 1; i < lines.Count; i++)
        {
            var d = lines[i].Top - lines[i - 1].Top;
            if (d > 0 && d < bodyHeight * 4) deltas.Add(d);
        }
        double lineStep = Median(deltas) ?? bodyHeight * 1.2;
        double bodyLeft = Percentile(lines.Select(l => l.Left), 0.15);
        double bodyRight = Percentile(lines.Select(l => l.Right), 0.85);
        double bodyWidth = Math.Max(1, bodyRight - bodyLeft);

        var current = new Paragraph();
        current.Lines.Add(lines[0]);
        for (int i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var line = lines[i];
            if (IsParagraphBreak(prev, line, lineStep, bodyLeft, bodyRight, bodyWidth, bodyHeight))
            {
                result.Add(current);
                current = new Paragraph();
            }
            current.Lines.Add(line);
        }
        result.Add(current);
        return result;
    }

    private static bool IsParagraphBreak(PdfTextLine prev, PdfTextLine line, double lineStep, double bodyLeft,
        double bodyRight, double bodyWidth, double bodyHeight)
    {
        double step = line.Top - prev.Top;
        // Moved up the page or sideways into another column
        if (step <= -bodyHeight * 0.5) return true;
        // Extra vertical space
        if (step > lineStep * 1.6) return true;
        // A big change in font size (heading -> body or vice versa)
        if (prev.Height > 0 && line.Height > 0 && !StartsLower(line.Text) && !EndsHyphen(prev.Text) &&
            Math.Max(prev.Height, line.Height) / Math.Min(prev.Height, line.Height) > 1.6) return true;

        // A line starting in lower case continues the sentence unless there was extra space above it
        if (StartsLower(line.Text)) return false;

        var prevText = prev.Text.TrimEnd();
        bool prevEndsSentence = prevText.Length > 0 && TerminalPunctuation.Contains(prevText[^1]);
        bool prevShort = prev.Right < bodyRight - bodyWidth * 0.12;
        // A first-line indent on this line, while the previous line sat on the margin
        bool indented = line.Left > bodyLeft + bodyHeight * 0.8 && line.Left < bodyLeft + bodyWidth * 0.3 &&
                        prev.Left <= bodyLeft + bodyHeight * 0.5;
        if (indented && (prevEndsSentence || prevShort)) return true;
        // The previous line stopped short and finished a sentence
        if (prevShort && prevEndsSentence) return true;
        return false;
    }

    private static bool StartsLower(string text)
    {
        var t = text.TrimStart();
        return t.Length > 0 && char.IsLower(t[0]);
    }

    private static bool EndsHyphen(string text)
    {
        var t = text.TrimEnd();
        return t.Length > 0 && t[^1] is '-' or '­' or '¬' or '‐';
    }

    private static bool ContinuesOnNextPage(Paragraph last, Paragraph next)
    {
        var lastText = last.Lines[^1].Text.TrimEnd();
        if (lastText.Length == 0) return false;
        char end = lastText[^1];
        if (end is '-' or '­' or '¬') return true;
        if (TerminalPunctuation.Contains(end)) return false;
        var nextText = next.Lines[0].Text.TrimStart();
        // A heading on the next page is never a continuation
        if (next.Height > last.Height * 1.6 && !StartsLower(next.Lines[0].Text)) return false;
        return nextText.Length > 0 && !char.IsUpper(nextText[0]) || char.IsLetterOrDigit(end) || end == ',';
    }

    private static void StripHeadersFooters(IReadOnlyList<PdfTextPage> pages, List<List<PdfTextLine>> pageLines)
    {
        // Only the first/last two lines of a page in the top/bottom 10% are candidates
        var candidates = new List<(int page, PdfTextLine line, bool top)>();
        for (int p = 0; p < pageLines.Count; p++)
        {
            var lines = pageLines[p];
            double h = pages[p].Height;
            for (int i = 0; i < Math.Min(2, lines.Count); i++)
            {
                if (lines[i].Bottom < h * 0.10) candidates.Add((p, lines[i], true));
            }
            for (int i = Math.Max(0, lines.Count - 2); i < lines.Count; i++)
            {
                if (lines[i].Top > h * 0.90) candidates.Add((p, lines[i], false));
            }
        }

        // Running heads repeat (ignoring page numbers) on several pages in the same zone
        var counts = candidates
            .GroupBy(c => (c.top, key: Normalize(c.line.Text)))
            .ToDictionary(g => g.Key, g => g.Select(x => x.page).Distinct().Count());
        // Verso/recto heads alternate, so each variant appears on every other page
        int threshold = Math.Max(2, pageLines.Count / 6);

        foreach (var (page, line, top) in candidates)
        {
            var key = Normalize(line.Text);
            bool pageNumber = PageNumberRegex().IsMatch(line.Text.Trim());
            bool repeated = key.Length > 0 && counts[(top, key)] >= threshold;
            if (pageNumber || repeated)
            {
                pageLines[page].Remove(line);
            }
        }
    }

    private static string Normalize(string text) =>
        DigitsAndSpaceRegex().Replace(text.ToLowerInvariant(), "");

    private static double? Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(x => x).ToList();
        return list.Count == 0 ? null : list[list.Count / 2];
    }

    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var list = values.OrderBy(x => x).ToList();
        if (list.Count == 0) return 0;
        return list[Math.Clamp((int) (list.Count * fraction), 0, list.Count - 1)];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\d\s\W]+")]
    private static partial Regex DigitsAndSpaceRegex();

    // "12", "- 12 -", "[12]", "Page 12", "p. 12", "xiv"
    [GeneratedRegex(@"^[\W_]*(page|p\.|pg\.?)?\s*([0-9]{1,4}|[ivxlcdm]{1,7})[\W_]*$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumberRegex();
}
