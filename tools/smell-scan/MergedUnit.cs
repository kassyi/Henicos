using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A measured unit ready to be judged: the rule, the measurement, the parameters that apply and the other places.</summary>
/// <param name="Rule">The rule that applies.</param>
/// <param name="Measurement">The measurement, with the values of every part added up for a unit spread over files.</param>
/// <param name="Parameters">The parameters of the rule for the file of the measurement.</param>
/// <param name="Related">The other parts of a unit spread over files; empty otherwise.</param>
internal sealed record MergedUnit(
    SmellRule Rule,
    Measurement Measurement,
    RuleParameters Parameters,
    ImmutableArray<RelatedLocation> Related);
