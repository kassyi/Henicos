using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Writes a report as plain text for a person or an agent that reads a terminal. Findings are grouped by file, each as one
/// line with the size, the place and the symbol, the way the review hot-spot listing reads. A run that asked for metrics
/// lists the largest measurements of each rule instead. The writer only formats.
/// </summary>
internal static class TextReportWriter
{
    /// <summary>Returns the findings of <paramref name="report"/> as UTF-8 text without a byte order mark.</summary>
    public static byte[] WriteFindings(ScanReport report)
    {
        var text = new StringBuilder();
        string? currentPath = null;
        foreach (var finding in report.Findings)
        {
            var measurement = finding.Measurement;
            if (!string.Equals(measurement.Path, currentPath, StringComparison.Ordinal))
            {
                text.Append("== ").Append(measurement.Path).Append('\n');
                currentPath = measurement.Path;
            }

            AppendLine(text, measurement, finding.RuleId, string.Empty);
            foreach (var related in finding.RelatedLocations)
            {
                text.Append("        also ").Append(related.Path).Append(':')
                    .Append(related.Range.StartLine.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            files.Add(finding.Measurement.Path);
        }

        text.Append(CultureInfo.InvariantCulture, $"-- {report.Findings.Length} findings in {files.Count} files ")
            .Append(CultureInfo.InvariantCulture, $"({report.Files} files measured)\n");
        return Encode(text);
    }

    /// <summary>Returns the largest measurements of each rule of <paramref name="report"/> as UTF-8 text.</summary>
    public static byte[] WriteMetrics(ScanReport report)
    {
        var text = new StringBuilder();
        string? currentGroup = null;
        foreach (var metric in report.Metrics)
        {
            var group = metric.RuleId + " (" + metric.Measurement.Metric + ")";
            if (!string.Equals(group, currentGroup, StringComparison.Ordinal))
            {
                text.Append("== ").Append(group).Append('\n');
                currentGroup = group;
            }

            var mark = metric.Exceeds ? $"  > {metric.Threshold}" : string.Empty;
            AppendLine(text, metric.Measurement, metric.Measurement.Path, mark);
        }

        text.Append(CultureInfo.InvariantCulture, $"-- {report.Files} files measured\n");
        return Encode(text);
    }

    private static void AppendLine(StringBuilder text, Measurement measurement, string label, string suffix)
    {
        var range = measurement.Range;
        text.Append("  ")
            .Append(measurement.Value.ToString(CultureInfo.InvariantCulture).PadLeft(4))
            .Append(' ').Append(MetricText.Unit(measurement.Metric).PadRight(8))
            .Append(' ').Append(range.StartLine.ToString(CultureInfo.InvariantCulture))
            .Append('-').Append(range.EndLine.ToString(CultureInfo.InvariantCulture))
            .Append("  ").Append(label).Append("  ")
            .Append(measurement.Symbol).Append(suffix).Append('\n');
    }

    private static byte[] Encode(StringBuilder text) => new UTF8Encoding(false).GetBytes(text.ToString());
}
