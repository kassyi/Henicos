using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Writes a report as JSON. The writer only formats: keys come in a fixed order, maps are sorted ordinally, and there
/// is no timestamp, so the same report gives the same bytes. Findings are grouped by file so a reader can take the
/// key of the file it reviews.
/// </summary>
internal static class JsonReportWriter
{
    /// <summary>Returns the report as UTF-8 without a byte order mark, ending with a line feed.</summary>
    public static byte[] Write(ScanReport report)
    {
        using var stream = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using (var json = new Utf8JsonWriter(stream, options))
        {
            WriteReport(json, report);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteReport(Utf8JsonWriter json, ScanReport report)
    {
        json.WriteStartObject();
        json.WriteString("tool", "smell-scan");
        json.WriteNumber("schema", 1);
        if (report.Commit is not null)
        {
            json.WriteString("commit", report.Commit);
        }

        json.WriteString("config", report.ConfigPath);
        json.WriteString("configHash", report.ConfigHash);

        json.WriteStartObject("scope");
        json.WriteNumber("files", report.Files);
        json.WriteNumber("excludedGenerated", report.ExcludedGenerated);
        json.WriteString("tier", report.Tier);
        WriteUnmeasured(json, report);
        json.WriteEndObject();

        WriteSummary(json, report);
        WriteFiles(json, report);
        json.WriteEndObject();
    }

    private static void WriteUnmeasured(Utf8JsonWriter json, ScanReport report)
    {
        var count = 0;
        foreach (var file in report.Unmeasured)
        {
            count += file.Lines.Length;
        }

        json.WriteNumber("unmeasuredBranches", count);
        json.WriteStartObject("unmeasuredBranchFiles");
        foreach (var file in report.Unmeasured)
        {
            json.WriteStartArray(file.Path);
            foreach (var line in file.Lines)
            {
                json.WriteNumberValue(line);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteSummary(Utf8JsonWriter json, ScanReport report)
    {
        var byRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in report.RuleIds)
        {
            byRule[id] = 0;
        }

        var byProject = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            byRule[finding.RuleId]++;
            var project = report.ProjectByPath[finding.Measurement.Path];
            byProject.TryGetValue(project, out var count);
            byProject[project] = count + 1;
        }

        json.WriteStartObject("summary");
        WriteCounts(json, "byRule", byRule);
        WriteCounts(json, "byProject", byProject);
        json.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter json, string name, SortedDictionary<string, int> counts)
    {
        json.WriteStartObject(name);
        foreach (var pair in counts)
        {
            json.WriteNumber(pair.Key, pair.Value);
        }

        json.WriteEndObject();
    }

    private static void WriteFiles(Utf8JsonWriter json, ScanReport report)
    {
        json.WriteStartObject("files");
        string? currentPath = null;
        foreach (var finding in report.Findings)
        {
            var path = finding.Measurement.Path;
            if (!string.Equals(path, currentPath, StringComparison.Ordinal))
            {
                if (currentPath is not null)
                {
                    json.WriteEndArray();
                }

                json.WriteStartArray(path);
                currentPath = path;
            }

            WriteFinding(json, finding);
        }

        if (currentPath is not null)
        {
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteFinding(Utf8JsonWriter json, Finding finding)
    {
        var measurement = finding.Measurement;
        json.WriteStartObject();
        json.WriteString("rule", finding.RuleId);
        json.WriteString("severity", SeverityName(finding.Severity));
        json.WriteNumber("startLine", measurement.Range.StartLine);
        json.WriteNumber("startColumn", measurement.Range.StartColumn);
        json.WriteNumber("endLine", measurement.Range.EndLine);
        json.WriteNumber("endColumn", measurement.Range.EndColumn);
        json.WriteString("symbol", measurement.Symbol);
        json.WriteString("metric", measurement.Metric);
        json.WriteNumber("value", measurement.Value);
        json.WriteNumber("threshold", finding.Threshold);
        if (measurement.PhysicalLines > 0)
        {
            json.WriteNumber("physicalLines", measurement.PhysicalLines);
        }

        json.WriteString("suggestion", finding.Suggestion);
        json.WriteString("fingerprint", finding.Fingerprint);
        json.WriteEndObject();
    }

    private static string SeverityName(SmellSeverity severity) => severity switch
    {
        SmellSeverity.Note => "note",
        SmellSeverity.Warning => "warning",
        SmellSeverity.Error => "error",
        _ => "off",
    };
}
