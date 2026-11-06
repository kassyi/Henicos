namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>One measured unit for the metrics listing, whether or not it is above the threshold.</summary>
/// <param name="RuleId">The rule whose detector measured it.</param>
/// <param name="Measurement">What was measured and where.</param>
/// <param name="Threshold">The largest accepted value.</param>
/// <param name="Exceeds">True when the measurement would be a finding.</param>
internal sealed record RuleMetric(string RuleId, Measurement Measurement, int Threshold, bool Exceeds);
