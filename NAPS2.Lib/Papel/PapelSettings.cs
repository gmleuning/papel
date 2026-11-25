using System.Text.Json;
using System.Text.Json.Serialization;

namespace NAPS2.Papel;

/// <summary>
/// A PowerShell command that the Tools menu can run on the current pages.
/// </summary>
public record PapelTool
{
    /// <summary>Menu label.</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The PowerShell command. Placeholders: {in} = the pages exported to a temp PDF, {out} = the expected output
    /// path, {dir} = the temp folder, {name} = the input file name without extension.
    /// </summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// The file the command writes, with the same placeholders. If it exists after the command, it replaces the pages
    /// the tool ran on. Leave empty for commands that don't produce a PDF (the output just shows in the console).
    /// </summary>
    public string Output { get; init; } = "";

    /// <summary>Run on the selected pages (true) or all pages (false).</summary>
    public bool SelectedOnly { get; init; } = true;

    /// <summary>Whether NAPS2 should OCR the pages when exporting them for the tool.</summary>
    public bool OcrInput { get; init; }
}

/// <summary>
/// Settings for the Papel additions, kept in their own JSON file (%APPDATA%\NAPS2\papel.json) so they don't
/// interfere with NAPS2's own config and can be edited by hand.
/// </summary>
public class PapelSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string FilePath => Path.Combine(Paths.AppData, "papel.json");

    /// <summary>Drop running headers, footers and page numbers from clean text.</summary>
    public bool StripHeadersFooters { get; set; } = true;

    /// <summary>File name template used when book info is set. $(author), $(title).</summary>
    public string FileNameTemplate { get; set; } = "$(author) - $(title)";

    /// <summary>The PowerShell executable. Empty = pwsh on PATH, falling back to Windows PowerShell.</summary>
    public string PowerShellPath { get; set; } = "";

    /// <summary>Whether the console pane was open when the app last closed.</summary>
    public bool ConsoleVisible { get; set; }

    public int ConsoleHeight { get; set; } = 220;

    public int ThresholdC { get; set; } = 15;

    public int ThresholdBlock { get; set; } = 61;

    public bool ThresholdFlatten { get; set; } = true;

    public double MarginMm { get; set; } = 3;

    public List<PapelTool> Tools { get; set; } = DefaultTools();

    public static List<PapelTool> DefaultTools() =>
    [
        new()
        {
            Name = "repdf (clean B/W, keep text)",
            Command = "repdf '{in}' -Force",
            Output = "{dir}\\{name}_repdf.pdf"
        },
        new()
        {
            Name = "repdf -Reocr (fresh OCR)",
            Command = "repdf '{in}' -Reocr -Force",
            Output = "{dir}\\{name}_repdf.pdf"
        },
        new()
        {
            Name = "repdf -Reocr -Fra",
            Command = "repdf '{in}' -Reocr -Fra -Force",
            Output = "{dir}\\{name}_repdf.pdf"
        },
        new()
        {
            Name = "repdf -Reocr -Kor",
            Command = "repdf '{in}' -Reocr -Kor -Force",
            Output = "{dir}\\{name}_repdf.pdf"
        },
        new()
        {
            Name = "smallit (OCR + shrink)",
            Command = "smallit '{in}' '{out}' -Force",
            Output = "{dir}\\{name}_small.pdf"
        }
    ];

    private static PapelSettings? _instance;

    public static PapelSettings Current => _instance ??= Load();

    public static PapelSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<PapelSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings != null)
                {
                    _instance = settings;
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.ErrorException("Could not read papel.json, using defaults", ex);
        }
        _instance = new PapelSettings();
        _instance.Save();
        return _instance;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.ErrorException("Could not write papel.json", ex);
        }
    }
}

/// <summary>
/// State for the current document that isn't stored with the pages.
/// </summary>
public class PapelSession
{
    public string Title { get; set; } = "";

    public string Author { get; set; } = "";

    public bool HasBookInfo => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Author);

    /// <summary>
    /// Builds a file name (without extension) from the template, or null if no book info is set.
    /// </summary>
    public string? GetFileName()
    {
        if (!HasBookInfo) return null;
        var name = PapelSettings.Current.FileNameTemplate
            .Replace("$(author)", Author.Trim())
            .Replace("$(title)", Title.Trim())
            .Trim(' ', '-', '_', '.');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
