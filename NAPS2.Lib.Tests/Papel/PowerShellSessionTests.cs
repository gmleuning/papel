using NAPS2.Papel;
using Xunit;

namespace NAPS2.Lib.Tests.Papel;

public class PowerShellSessionTests
{
    [Fact]
    public async Task RunsCommandsAndCapturesOutput()
    {
        using var shell = new PowerShellSession();
        var lines = new List<ConsoleLine>();
        shell.LineReceived += (_, l) =>
        {
            lock (lines) lines.Add(l);
        };

        var run = shell.RunAndWait("Write-Output ('papel' + '-ok'); Write-Output (2+3)");
        await run.WaitAsync(TimeSpan.FromSeconds(60));

        lock (lines)
        {
            Assert.Contains(lines, l => l.Text == "papel-ok");
            Assert.Contains(lines, l => l.Text == "5");
            Assert.DoesNotContain(lines, l => l.Text.Contains("__PAPEL_DONE_"));
        }
    }

    [Fact]
    public async Task PapelImportRaisesEvent()
    {
        using var shell = new PowerShellSession();
        var path = Path.GetTempFileName();
        string? requested = null;
        shell.ImportRequested += (_, p) => requested = p;

        await shell.RunAndWait($"papel-import {PowerShellSession.Quote(path)}").WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(Path.GetFullPath(path), requested);
        File.Delete(path);
    }
}
