#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>Two findings of one rule at one symbol that differ in metric keep their own fingerprints.</summary>
public class FingerprintMetricTests
{
    private const string Config = "Config/swallowed-all.yml";

    private static string FingerprintOf(ScanReport report, string metric)
    {
        var matches = new List<Finding>();
        foreach (var finding in report.Findings)
        {
            if (finding.Measurement.Metric == metric)
            {
                matches.Add(finding);
            }
        }

        return Assert.Single(matches).Fingerprint;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_EmptyCatchBesideALogOnlyCatch_RemovingTheEmptyOneKeepsTheOtherFingerprint()
    {
        using var withBoth = new ScanWorkspace();
        withBoth.Add("SwallowedBoth.cs", "src/SwallowedPair.cs");
        using var withOne = new ScanWorkspace();
        withOne.Add("SwallowedLogOnly.cs", "src/SwallowedPair.cs");

        var both = withBoth.Scan(Config, 1, "src/SwallowedPair.cs");
        var one = withOne.Scan(Config, 1, "src/SwallowedPair.cs");

        Assert.Equal(2, both.Findings.Length);
        Assert.NotEqual(FingerprintOf(both, "emptyCatch"), FingerprintOf(both, "logOnlyCatch"));
        Assert.Equal(FingerprintOf(both, "logOnlyCatch"), FingerprintOf(one, "logOnlyCatch"));
    }
}
