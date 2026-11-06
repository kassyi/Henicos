using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Writes findings as GitHub Actions workflow commands so that they show up on the changed lines of a pull request.
/// Every finding is a warning, whatever the severity of its rule: the report is advice and must never fail a run.
/// The writer only formats.
/// </summary>
internal static class GithubAnnotationWriter
{
    /// <summary>Returns the findings of <paramref name="report"/> as UTF-8 text without a byte order mark.</summary>
    public static byte[] Write(ScanReport report)
    {
        var text = new StringBuilder();
        foreach (var finding in report.Findings)
        {
            var range = finding.Measurement.Range;
            text.Append("::warning file=").Append(EscapeProperty(finding.Measurement.Path))
                .Append(",line=").Append(range.StartLine.ToString(CultureInfo.InvariantCulture))
                .Append(",endLine=").Append(range.EndLine.ToString(CultureInfo.InvariantCulture))
                .Append(",title=").Append(EscapeProperty("smell-scan " + finding.RuleId))
                .Append("::").Append(EscapeData(MetricText.Message(finding))).Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"smell-scan: {report.Findings.Length} findings\n");
        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    private static string EscapeData(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);

    private static string EscapeProperty(string value) =>
        EscapeData(value)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);
}
