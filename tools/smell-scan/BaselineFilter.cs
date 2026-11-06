using System.Collections.Immutable;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Keeps the findings of a report that an earlier JSON report does not have, matched by fingerprint. A finding that
/// moved to another line or file keeps its fingerprint and so is not new; one that grew past the threshold since the
/// earlier run is new. The earlier report must come from the same configuration, or findings can appear only because a
/// limit changed.
/// </summary>
internal static class BaselineFilter
{
    /// <summary>Returns <paramref name="report"/> without the findings whose fingerprint is in <paramref name="baselineJson"/>.</summary>
    /// <exception cref="UsageException">The text is not a report written by this tool.</exception>
    public static ScanReport KeepNew(ScanReport report, string baselineJson)
    {
        var known = Fingerprints(baselineJson);
        var kept = ImmutableArray.CreateBuilder<Finding>();
        foreach (var finding in report.Findings)
        {
            if (!known.Contains(finding.Fingerprint))
            {
                kept.Add(finding);
            }
        }

        return report with { Findings = kept.ToImmutable() };
    }

    private static HashSet<string> Fingerprints(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new UsageException("The baseline is not valid JSON: " + exception.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            var isReport = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("tool", out var tool)
                && tool.ValueKind == JsonValueKind.String
                && tool.GetString() == "smell-scan"
                && root.TryGetProperty("files", out var files)
                && files.ValueKind == JsonValueKind.Object;
            if (!isReport)
            {
                throw new UsageException("The baseline is not a JSON report of smell-scan.");
            }

            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in root.GetProperty("files").EnumerateObject())
            {
                foreach (var finding in file.Value.EnumerateArray())
                {
                    var hasFingerprint = finding.TryGetProperty("fingerprint", out var fingerprint);
                    if (hasFingerprint && fingerprint.GetString() is { } text)
                    {
                        fingerprints.Add(text);
                    }
                }
            }

            return fingerprints;
        }
    }
}
