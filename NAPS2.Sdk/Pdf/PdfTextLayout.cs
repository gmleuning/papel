using System.Text;
using NAPS2.Pdf.Pdfium;

namespace NAPS2.Pdf;

/// <summary>
/// A line of text read from a PDF text layer, in points with the origin at the top-left of the page.
/// </summary>
public record PdfTextLine(string Text, double Left, double Top, double Right, double Bottom)
{
    public double Height => Bottom - Top;
}

/// <summary>
/// A page's worth of text lines plus the page size, in points.
/// </summary>
public record PdfTextPage(double Width, double Height, IReadOnlyList<PdfTextLine> Lines);

/// <summary>
/// Reads the text layer of a PDF as positioned lines, which is what paragraph reflow needs (plain text extraction
/// loses the geometry that tells a wrapped line from a real paragraph break).
/// </summary>
public static class PdfTextLayout
{
    public static List<PdfTextPage> ReadLines(string path, string? password = null)
    {
        var pages = new List<PdfTextPage>();
        lock (PdfiumNativeLibrary.Instance)
        {
            using var doc = PdfDocument.Load(path, password);
            for (int i = 0; i < doc.PageCount; i++)
            {
                using var page = doc.GetPage(i);
                using var text = page.GetText();
                pages.Add(new PdfTextPage(page.Width, page.Height, ReadPageLines(text, page.Height)));
            }
        }
        return pages;
    }

    private static List<PdfTextLine> ReadPageLines(PdfText text, double pageHeight)
    {
        var lines = new List<PdfTextLine>();
        var sb = new StringBuilder();
        double left = double.MaxValue, right = double.MinValue, top = double.MaxValue, bottom = double.MinValue;

        void Flush()
        {
            var s = sb.ToString().Trim();
            if (s.Length > 0 && left < double.MaxValue)
            {
                lines.Add(new PdfTextLine(s, left, top, right, bottom));
            }
            sb.Clear();
            left = top = double.MaxValue;
            right = bottom = double.MinValue;
        }

        int count = text.CharCount;
        for (int i = 0; i < count; i++)
        {
            int cp = text.ReadUtf32(i);
            if (cp == '\r') continue;
            if (cp == '\n')
            {
                Flush();
                continue;
            }
            string str = cp is >= 0xD800 and <= 0xDFFF ? ((char) cp).ToString() : char.ConvertFromUtf32(cp);
            if (str.Length == 1 && char.IsControl(str[0]) && !char.IsWhiteSpace(str[0])) continue;
            sb.Append(str);
            if (char.IsWhiteSpace(str, 0)) continue;
            try
            {
                var box = text.GetCharBox(i);
                // PDF space has y going up; flip to top-down
                left = Math.Min(left, box.Left);
                right = Math.Max(right, box.Right);
                top = Math.Min(top, pageHeight - box.Top);
                bottom = Math.Max(bottom, pageHeight - box.Bottom);
            }
            catch (Exception)
            {
                // Generated characters have no box; ignore
            }
        }
        Flush();
        return lines;
    }
}
