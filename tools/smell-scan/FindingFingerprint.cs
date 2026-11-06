using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Gives each finding an identity made of the rule, the qualified symbol and the position among findings of the same
/// rule and symbol. The path and the line are not part of it, so moving a type to another file or shifting its lines
/// keeps the fingerprint, and a before/after comparison counts it as the same finding.
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
            var key = finding.RuleId + "|" + finding.Measurement.Symbol;
            seen.TryGetValue(key, out var ordinal);
            seen[key] = ordinal + 1;
            result.Add(finding with { Fingerprint = Fnv1A64.Compute(key + "|" + ordinal).ToString("x16") });
        }

        return result;
    }
}
