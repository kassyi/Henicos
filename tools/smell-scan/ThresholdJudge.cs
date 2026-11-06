namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Compares measurements with the threshold of a rule and turns the ones above it into findings.</summary>
internal static class ThresholdJudge
{
    /// <summary>
    /// Judges <paramref name="measurements"/> of the detector of <paramref name="rule"/> under <paramref name="parameters"/>.
    /// The fingerprint of a finding is left empty; it needs the position of the finding among its equals.
    /// </summary>
    public static List<Finding> Judge(SmellRule rule, RuleParameters parameters, IReadOnlyList<Measurement> measurements)
    {
        var findings = new List<Finding>();
        var threshold = parameters.Get(rule.Detector.ThresholdParameter);
        foreach (var measurement in measurements)
        {
            if (rule.Detector.Exceeds(measurement, parameters))
            {
                findings.Add(new Finding(rule.Id, rule.Severity, measurement, threshold, rule.Suggestion, string.Empty));
            }
        }

        return findings;
    }
}
