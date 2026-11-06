using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Writes a report as SARIF 2.1.0, the form that code-scanning tools and editors read. Paths are relative to the
/// <c>SRCROOT</c> base, the identity that survives a move is the <c>smellScan/v1</c> partial fingerprint, and the other
/// parts of a unit spread over files are related locations. The writer only formats: keys come in a fixed order and
/// nothing depends on the time or the machine, so the same report gives the same bytes.
/// </summary>
internal static class SarifReportWriter
{
    /// <summary>The name of the partial fingerprint that holds <see cref="Finding.Fingerprint"/>.</summary>
    public const string FingerprintKey = "smellScan/v1";

    private const string BaseId = "SRCROOT";

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
            WriteLog(json, report);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteLog(Utf8JsonWriter json, ScanReport report)
    {
        json.WriteStartObject();
        json.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
        json.WriteString("version", "2.1.0");
        json.WriteStartArray("runs");
        json.WriteStartObject();
        WriteTool(json, report);
        json.WriteStartObject("originalUriBaseIds");
        json.WriteStartObject(BaseId);
        json.WriteStartObject("description");
        json.WriteString("text", "The repository root.");
        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteString("columnKind", "utf16CodeUnits");
        WriteRunProperties(json, report);
        json.WriteStartArray("results");
        foreach (var finding in report.Findings)
        {
            WriteResult(json, report, finding);
        }

        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteTool(Utf8JsonWriter json, ScanReport report)
    {
        json.WriteStartObject("tool");
        json.WriteStartObject("driver");
        json.WriteString("name", "smell-scan");
        json.WriteStartArray("rules");
        foreach (var rule in report.Rules)
        {
            WriteRule(json, rule);
        }

        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteRule(Utf8JsonWriter json, RuleInfo rule)
    {
        json.WriteStartObject();
        json.WriteString("id", rule.Id);
        json.WriteString("name", rule.Detector);
        json.WriteStartObject("shortDescription");
        json.WriteString("text", rule.Description);
        json.WriteEndObject();
        if (rule.Suggestion.Length > 0)
        {
            json.WriteStartObject("help");
            json.WriteString("text", rule.Suggestion);
            json.WriteEndObject();
        }

        json.WriteStartObject("defaultConfiguration");
        json.WriteString("level", Level(rule.Severity));
        json.WriteEndObject();
        json.WriteStartObject("properties");
        if (rule.Help.Length > 0)
        {
            json.WriteString("guidance", rule.Help);
        }

        json.WriteStartObject("parameters");
        foreach (var pair in rule.Parameters)
        {
            json.WriteNumber(pair.Key, pair.Value);
        }

        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteRunProperties(Utf8JsonWriter json, ScanReport report)
    {
        json.WriteStartObject("properties");
        if (report.Commit is not null)
        {
            json.WriteString("commit", report.Commit);
        }

        json.WriteString("config", report.ConfigPath);
        json.WriteString("configHash", report.ConfigHash);
        json.WriteNumber("filesMeasured", report.Files);
        json.WriteEndObject();
    }

    private static void WriteResult(Utf8JsonWriter json, ScanReport report, Finding finding)
    {
        var measurement = finding.Measurement;
        json.WriteStartObject();
        json.WriteString("ruleId", finding.RuleId);
        json.WriteNumber("ruleIndex", RuleIndex(report, finding.RuleId));
        json.WriteString("level", Level(finding.Severity));
        json.WriteStartObject("message");
        json.WriteString("text", MetricText.Message(finding));
        json.WriteEndObject();

        json.WriteStartArray("locations");
        json.WriteStartObject();
        WritePhysicalLocation(json, measurement.Path, measurement.Range);
        if (measurement.Symbol.Length > 0)
        {
            json.WriteStartArray("logicalLocations");
            json.WriteStartObject();
            json.WriteString("fullyQualifiedName", measurement.Symbol);
            json.WriteEndObject();
            json.WriteEndArray();
        }

        json.WriteEndObject();
        json.WriteEndArray();

        if (finding.RelatedLocations.Length > 0)
        {
            json.WriteStartArray("relatedLocations");
            var id = 1;
            foreach (var related in finding.RelatedLocations)
            {
                json.WriteStartObject();
                json.WriteNumber("id", id++);
                WritePhysicalLocation(json, related.Path, related.Range);
                json.WriteStartObject("message");
                json.WriteString("text", related.Message);
                json.WriteEndObject();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        json.WriteStartObject("partialFingerprints");
        json.WriteString(FingerprintKey, finding.Fingerprint);
        json.WriteEndObject();

        json.WriteStartObject("properties");
        json.WriteString("metric", measurement.Metric);
        json.WriteNumber("value", measurement.Value);
        json.WriteNumber("threshold", finding.Threshold);
        if (measurement.PhysicalLines > 0)
        {
            json.WriteNumber("physicalLines", measurement.PhysicalLines);
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WritePhysicalLocation(Utf8JsonWriter json, string path, SourceRange range)
    {
        json.WriteStartObject("physicalLocation");
        json.WriteStartObject("artifactLocation");
        json.WriteString("uri", EscapePath(path));
        json.WriteString("uriBaseId", BaseId);
        json.WriteEndObject();
        json.WriteStartObject("region");
        json.WriteNumber("startLine", range.StartLine);
        json.WriteNumber("startColumn", range.StartColumn);
        json.WriteNumber("endLine", range.EndLine);
        json.WriteNumber("endColumn", range.EndColumn);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static int RuleIndex(ScanReport report, string ruleId)
    {
        for (var i = 0; i < report.Rules.Length; i++)
        {
            if (string.Equals(report.Rules[i].Id, ruleId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"The finding names rule '{ruleId}', which the report does not list.");
    }

    private static string Level(SmellSeverity severity) => severity switch
    {
        SmellSeverity.Error => "error",
        SmellSeverity.Warning => "warning",
        _ => "note",
    };

    private static string EscapePath(string path)
    {
        var escaped = new List<string>();
        for (var start = 0; start <= path.Length;)
        {
            var end = path.IndexOf('/', start);
            if (end < 0)
            {
                end = path.Length;
            }

            escaped.Add(Uri.EscapeDataString(path[start..end]));
            start = end + 1;
        }

        return string.Join('/', escaped);
    }
}
