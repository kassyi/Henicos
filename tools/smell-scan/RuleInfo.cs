using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>What a report says about a rule that ran.</summary>
/// <param name="Id">The rule id.</param>
/// <param name="Detector">The name of its detector.</param>
/// <param name="Description">What the detector looks for.</param>
/// <param name="Severity">The severity of its findings.</param>
/// <param name="Suggestion">The advice of the rule.</param>
/// <param name="Help">A pointer to the guidance on the smell; empty when there is none.</param>
/// <param name="Parameters">The parameter values of the rule before any override, sorted by name.</param>
internal sealed record RuleInfo(
    string Id,
    string Detector,
    string Description,
    SmellSeverity Severity,
    string Suggestion,
    string Help,
    ImmutableArray<KeyValuePair<string, int>> Parameters);
