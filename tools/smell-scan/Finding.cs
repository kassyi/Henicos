using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A measurement that exceeded the threshold of a rule.</summary>
/// <param name="RuleId">The rule that reported it.</param>
/// <param name="Severity">The severity of the rule.</param>
/// <param name="Measurement">What was measured and where.</param>
/// <param name="Threshold">The largest accepted value.</param>
/// <param name="Suggestion">The advice of the rule.</param>
/// <param name="Fingerprint">An identity that stays the same when the code moves between lines or files.</param>
/// <param name="Related">The other places of a finding that is spread over several; empty otherwise.</param>
internal sealed record Finding(
    string RuleId,
    SmellSeverity Severity,
    Measurement Measurement,
    int Threshold,
    string Suggestion,
    string Fingerprint,
    ImmutableArray<RelatedLocation> Related = default)
{
    /// <summary>Gets the other places of the finding, never an uninitialized array.</summary>
    public ImmutableArray<RelatedLocation> RelatedLocations => Related.IsDefault ? [] : Related;
}
