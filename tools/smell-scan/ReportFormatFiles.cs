namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The names under which a run that writes several formats stores each of them in the output directory.</summary>
internal static class ReportFormatFiles
{
    /// <summary>Returns the file name of <paramref name="format"/> in an output directory.</summary>
    public static string NameOf(ReportFormat format) => format switch
    {
        ReportFormat.Json => "smells.json",
        ReportFormat.Sarif => "smells.sarif",
        ReportFormat.Markdown => "smells.md",
        ReportFormat.Github => "smells.github.txt",
        _ => "smells.txt",
    };
}
