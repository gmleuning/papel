using System.Text;

namespace NAPS2.Papel;

public record ConsoleLine(string Text, ConsoleLineKind Kind);

public enum ConsoleLineKind
{
    Output,
    Error,
    Command,
    Info
}

/// <summary>
/// One long-lived PowerShell process (pwsh, with the user's $PROFILE loaded) that the console pane and the Tools menu
/// share. Commands are written to its stdin; stdout/stderr come back as lines.
///
/// The process runs with redirected streams rather than a real terminal, so interactive prompts (Read-Host, etc.) and
/// progress bars don't work, and colour is turned off.
/// </summary>
public class PowerShellSession : IDisposable
{
    private const string DoneMarker = "__PAPEL_DONE_";
    private const string ImportMarker = "__PAPEL_IMPORT__ ";

    private readonly object _lock = new();
    private readonly List<ConsoleLine> _history = [];
    private readonly Dictionary<string, TaskCompletionSource> _pending = new();
    private Process? _process;

    public PowerShellSession()
    {
        // Don't leave pwsh (or a running ocrmypdf) behind when the app exits
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }

    /// <summary>Raised (on a background thread) for every line of output, including echoed commands.</summary>
    public event EventHandler<ConsoleLine>? LineReceived;

    /// <summary>Raised when a script calls papel-import with a file to add to the page list.</summary>
    public event EventHandler<string>? ImportRequested;

    /// <summary>All lines so far, so a newly created console view can show the backlog.</summary>
    public IReadOnlyList<ConsoleLine> History
    {
        get
        {
            lock (_lock) return _history.ToList();
        }
    }

    public bool IsRunning => _process is { HasExited: false };

    public bool IsBusy
    {
        get
        {
            lock (_lock) return _pending.Count > 0;
        }
    }

    public string ExecutableName { get; private set; } = "";

    public void EnsureStarted()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            Start();
        }
    }

    private void Start()
    {
        var exe = FindPowerShell();
        ExecutableName = Path.GetFileNameWithoutExtension(exe);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            // -Command - reads commands from stdin; the profile is dot-sourced below so a broken profile only
            // prints an error instead of killing the session
            Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        psi.Environment["PAPEL"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => HandleLine(e.Data, false);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data, true);
        process.Exited += (_, _) =>
        {
            Add(new ConsoleLine("[PowerShell exited]", ConsoleLineKind.Info));
            FailPending();
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        Add(new ConsoleLine($"[{ExecutableName} started; loading profile]", ConsoleLineKind.Info));
        WriteRaw(string.Join("; ",
            "$ErrorActionPreference = 'Continue'",
            "$ProgressPreference = 'SilentlyContinue'",
            "if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }",
            "function papel-import([Parameter(Mandatory)][string]$Path) { " +
            "foreach ($p in (Resolve-Path -Path $Path)) { Write-Output ('" + ImportMarker + "' + $p.ProviderPath) } }",
            "if (Test-Path -LiteralPath $PROFILE) { try { . $PROFILE } catch { Write-Error $_ } }",
            "Write-Output '[ready - type commands below; $pdf is set when you run a tool or press Pages -> $pdf]'"));
    }

    private static string FindPowerShell()
    {
        var configured = PapelSettings.Current.PowerShellPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "pwsh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Bad PATH entry
            }
        }
        var programFiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(programFiles)) return programFiles;
        return "powershell.exe";
    }

    private void HandleLine(string? line, bool isError)
    {
        if (line == null) return;
        if (line.StartsWith(DoneMarker))
        {
            var id = line.Substring(DoneMarker.Length).Trim();
            TaskCompletionSource? tcs;
            lock (_lock)
            {
                if (_pending.Remove(id, out tcs) == false) tcs = null;
            }
            tcs?.TrySetResult();
            return;
        }
        if (line.StartsWith(ImportMarker))
        {
            ImportRequested?.Invoke(this, line.Substring(ImportMarker.Length).Trim());
            return;
        }
        Add(new ConsoleLine(line, isError ? ConsoleLineKind.Error : ConsoleLineKind.Output));
    }

    private void Add(ConsoleLine line)
    {
        lock (_lock)
        {
            _history.Add(line);
            if (_history.Count > 5000) _history.RemoveRange(0, 1000);
        }
        LineReceived?.Invoke(this, line);
    }

    /// <summary>
    /// Sends a command typed by the user. Doesn't wait for it to finish.
    /// </summary>
    public void Run(string command)
    {
        EnsureStarted();
        Add(new ConsoleLine("PS> " + command, ConsoleLineKind.Command));
        WriteRaw(command);
    }

    /// <summary>
    /// Sends a command and completes when it has finished.
    /// </summary>
    public Task RunAndWait(string command)
    {
        EnsureStarted();
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _pending[id] = tcs;
        Add(new ConsoleLine("PS> " + command, ConsoleLineKind.Command));
        WriteRaw(command);
        WriteRaw($"Write-Output '{DoneMarker}{id}'");
        return tcs.Task;
    }

    /// <summary>
    /// Sets a PowerShell variable to a string value without echoing.
    /// </summary>
    public void SetVariable(string name, string value)
    {
        EnsureStarted();
        WriteRaw($"${name} = {Quote(value)}");
        Add(new ConsoleLine($"[${name} = {value}]", ConsoleLineKind.Info));
    }

    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private void WriteRaw(string text)
    {
        try
        {
            _process!.StandardInput.WriteLine(text);
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            Add(new ConsoleLine("[Could not send to PowerShell: " + ex.Message + "]", ConsoleLineKind.Error));
        }
    }

    /// <summary>
    /// Kills the process (and anything it started, e.g. a stuck ocrmypdf) and starts a fresh one.
    /// </summary>
    public void Restart()
    {
        lock (_lock)
        {
            KillProcess();
            FailPending();
            Start();
        }
    }

    public void ClearHistory()
    {
        lock (_lock) _history.Clear();
    }

    private void FailPending()
    {
        List<TaskCompletionSource> pending;
        lock (_lock)
        {
            pending = _pending.Values.ToList();
            _pending.Clear();
        }
        foreach (var tcs in pending)
        {
            tcs.TrySetException(new OperationCanceledException("PowerShell stopped"));
        }
    }

    private void KillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Already gone
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        lock (_lock) KillProcess();
    }
}
