using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Gives each finding an identity made of the rule, the metric, the qualified symbol and, for a smell that a symbol can have
/// several times (a catch clause, a string literal, a switch, an if chain), the hash of the text of the reported node. Only
/// findings with the same key are numbered, in report order. The path and the line are not part of it, so moving a type to
/// another file or shifting its lines keeps the fingerprint, and adding one catch clause to a method makes only that
/// one new, whatever its place among the others.
/// </summary>
internal static class FindingFingerprint
{
    /// <summary>Returns <paramref name="sorted"/> with fingerprints; the findings must already be in report order.</summary>
    public static List<Finding> Assign(IReadOnlyList<Finding> sorted)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<Finding>(sorted.Count);
        foreach (var finding in sorted)
        {
            var measurement = finding.Measurement;
            // Attributes of the assembly and other places outside any member have no symbol name
            var symbol = measurement.Symbol.Length == 0 ? "<assembly>" : measurement.Symbol;
            var key = finding.RuleId + "|" + measurement.Metric + "|" + symbol;
            if (measurement.NodeKey is not null)
            {
                key += "|" + measurement.NodeKey;
            }

            seen.TryGetValue(key, out var ordinal);
            seen[key] = ordinal + 1;
            result.Add(finding with { Fingerprint = Fnv1A64.Compute(key + "|" + ordinal).ToString("x16") });
        }

        return result;
    }
}
