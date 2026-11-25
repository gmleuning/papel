using NAPS2.Papel;
using NAPS2.Pdf;
using Xunit;

namespace NAPS2.Lib.Tests.Papel;

public class TextReflowPdfTests(ITestOutputHelper output)
{
    [Fact]
    public void ReflowsRealPdfTextLayer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "NAPS2.Sdk.Tests"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "NAPS2.Sdk.Tests", "Resources", "ocr_test_output.pdf");

        var pages = PdfTextLayout.ReadLines(path);
        var text = TextReflow.Reflow(pages);
        output.WriteLine(text);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "papel_reflow_check.md"), text);

        Assert.NotEmpty(pages[0].Lines);
        // Lines are joined: no single newlines inside paragraphs
        Assert.DoesNotContain(text.Trim().Split("\n\n"), p => p.Contains('\n'));
    }
}
