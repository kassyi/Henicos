using System.Collections.Immutable;

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
        foreach (var measurement in measurements)
        {
            var finding = JudgeOne(rule, parameters, measurement, []);
            if (finding is not null)
            {
                findings.Add(finding);
            }
        }

        return findings;
    }

    /// <summary>Judges one measurement; <paramref name="related"/> are the other places of a unit spread over several files.</summary>
    public static Finding? JudgeOne(
        SmellRule rule,
        RuleParameters parameters,
        Measurement measurement,
        ImmutableArray<RelatedLocation> related) =>
        rule.Detector.Exceeds(measurement, parameters)
            ? new Finding(
                rule.Id,
                rule.Severity,
                measurement,
                rule.Detector.ThresholdOf(measurement, parameters),
                rule.Suggestion,
                string.Empty,
                related)
            : null;
}
