namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A measurement together with the rule that asked for it, before it is judged.</summary>
/// <param name="Rule">The rule whose detector measured it.</param>
/// <param name="Measurement">What was measured.</param>
internal sealed record Candidate(SmellRule Rule, Measurement Measurement);
