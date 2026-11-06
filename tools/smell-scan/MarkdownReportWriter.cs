using System.Globalization;
using System.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Writes a report as Markdown for a person who decides what to do with a whole code base: the number of findings by
/// rule, by project and by file, and the largest findings of each rule. The tables can be pasted into an issue. The
/// writer only formats; ranking is by measured value, then by the report order.
/// </summary>
internal static class MarkdownReportWriter
{
    private const int TopPerRule = 20;
    private const int TopFiles = 20;

    /// <summary>Returns the report as UTF-8 without a byte order mark, with line feeds.</summary>
    public static byte[] Write(ScanReport report)
    {
        var text = new StringBuilder();
        text.Append("# Smell scan\n\n");
        text.Append(CultureInfo.InvariantCulture, $"{report.Findings.Length} findings in {report.Files} files");
        if (report.ExcludedGenerated > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" ({report.ExcludedGenerated} generated files left out)");
        }

        text.Append('.');
        if (report.Commit is not null)
        {
            text.Append(" Commit `").Append(report.Commit).Append("`.");
        }

        text.Append("\n\n");
        WriteByRule(text, report);
        WriteByProject(text, report);
        WriteByFile(text, report);
        WriteRuleDetails(text, report);
        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    private static void WriteByRule(StringBuilder text, ScanReport report)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in report.RuleIds)
        {
            counts[id] = 0;
        }

        foreach (var finding in report.Findings)
        {
            counts[finding.RuleId]++;
        }

        text.Append("## Findings by rule\n\n| Rule | Findings |\n| :- | -: |\n");
        foreach (var pair in counts)
        {
            AppendCount(text, pair.Key, pair.Value);
        }

        text.Append('\n');
    }

    private static void WriteByProject(StringBuilder text, ScanReport report)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            var project = report.ProjectByPath[finding.Measurement.Path];
            counts.TryGetValue(project, out var count);
            counts[project] = count + 1;
        }

        text.Append("## Findings by project\n\n| Project | Findings |\n| :- | -: |\n");
        foreach (var pair in Ranked(counts))
        {
            AppendCount(text, pair.Key, pair.Value);
        }

        text.Append('\n');
    }

    private static void WriteByFile(StringBuilder text, ScanReport report)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            counts.TryGetValue(finding.Measurement.Path, out var count);
            counts[finding.Measurement.Path] = count + 1;
        }

        var ranked = Ranked(counts);
        var shown = Math.Min(ranked.Count, TopFiles);
        text.Append(CultureInfo.InvariantCulture, $"## Files with the most findings ({shown} of {ranked.Count})\n\n");
        text.Append("| File | Findings |\n| :- | -: |\n");
        for (var i = 0; i < shown; i++)
        {
            AppendCount(text, ranked[i].Key, ranked[i].Value);
        }

        text.Append('\n');
    }

    private static void WriteRuleDetails(StringBuilder text, ScanReport report)
    {
        var groups = new SortedDictionary<string, List<Finding>>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            var key = finding.RuleId + "\n" + finding.Measurement.Metric;
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(finding);
        }

        foreach (var pair in groups)
        {
            var findings = pair.Value;
            findings.Sort(ByValueDescending);
            var first = findings[0];
            var shown = Math.Min(findings.Count, TopPerRule);
            text.Append("## `").Append(first.RuleId).Append("` (")
                .Append(MetricText.Unit(first.Measurement.Metric)).Append(")\n\n");
            if (first.Suggestion.Length > 0)
            {
                text.Append(first.Suggestion).Append("\n\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"{findings.Count} findings, {shown} listed, largest first.\n\n");

            text.Append("| Value | Limit | Place | Symbol |\n| -: | -: | :- | :- |\n");
            for (var i = 0; i < shown; i++)
            {
                AppendFinding(text, findings[i]);
            }

            text.Append('\n');
        }
    }

    private static void AppendCount(StringBuilder text, string name, int count)
    {
        text.Append("| `").Append(name).Append("` | ")
            .Append(count.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
    }

    private static void AppendFinding(StringBuilder text, Finding finding)
    {
        var measurement = finding.Measurement;
        var range = measurement.Range;
        text.Append("| ").Append(measurement.Value.ToString(CultureInfo.InvariantCulture))
            .Append(" | ").Append(finding.Threshold.ToString(CultureInfo.InvariantCulture))
            .Append(" | `").Append(measurement.Path).Append(':')
            .Append(range.StartLine.ToString(CultureInfo.InvariantCulture))
            .Append('-').Append(range.EndLine.ToString(CultureInfo.InvariantCulture))
            .Append("` | ");
        if (measurement.Symbol.Length > 0)
        {
            text.Append('`').Append(measurement.Symbol.Replace("|", "\\|", StringComparison.Ordinal)).Append('`');
        }

        text.Append(" |\n");
    }

    private static int ByValueDescending(Finding x, Finding y)
    {
        var byValue = y.Measurement.Value.CompareTo(x.Measurement.Value);
        return byValue != 0 ? byValue : FindingOrder.Instance.Compare(x, y);
    }

    // Highest count first; equal counts by name, so the order is the same in every run.
    private static List<KeyValuePair<string, int>> Ranked(Dictionary<string, int> counts)
    {
        var ranked = new List<KeyValuePair<string, int>>(counts);
        ranked.Sort(static (x, y) =>
        {
            var byCount = y.Value.CompareTo(x.Value);
            return byCount != 0 ? byCount : string.CompareOrdinal(x.Key, y.Key);
        });
        return ranked;
    }
}
