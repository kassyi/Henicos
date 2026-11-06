#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class LongIfChainDetectorTests
{
    private const string Cases = "LongIfChainCases.cs";
    private const string Prefix = "Cases.LongIfChainCases.";
    private static readonly LongIfChainDetector s_detector = new();

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_IfElseIfElseIfElse_CountsEveryBranchIncludingTheFinalElse()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, Prefix + "FourBranches(int)");

        Assert.Equal("branches", measurement.Metric);
        Assert.Equal(4, measurement.Value);
        Assert.Equal(new SourceRange(7, 9, 22, 10), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_ChainWithoutFinalElse_CountsTheIfAndTheElseIfs()
    {
        Assert.Equal(3, DetectorHarness.Single(s_detector, Cases, Prefix + "NoFinalElse(int)").Value);
        Assert.Equal(7, DetectorHarness.Single(s_detector, Cases, Prefix + "Seven(int)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_TwoBranchesAndSeparateIfs_AreNotChains()
    {
        Assert.Equal(0, DetectorHarness.Count(s_detector, Cases, Prefix + "TwoBranches(int)"));
        Assert.Equal(0, DetectorHarness.Count(s_detector, Cases, Prefix + "Sequential(int)"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_IfInsideAnElseBlock_StartsAChainOfItsOwn()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, Prefix + "IfInsideElseBlock(int)");

        // The outer if has two branches, the if inside the else block has four
        Assert.Equal(4, measurement.Value);
        Assert.Equal(new SourceRange(57, 13, 60, 31), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_BranchesEqualToThreshold_AreNotReportedButOneMoreIs()
    {
        var atSix = DetectorHarness.ExceedingSymbols(s_detector, Cases, 6);
        var atFive = DetectorHarness.ExceedingSymbols(s_detector, Cases, 5);

        Assert.DoesNotContain(Prefix + "Seven(int)", DetectorHarness.ExceedingSymbols(s_detector, Cases, 7));
        Assert.Contains(Prefix + "Seven(int)", atSix);
        Assert.Contains(Prefix + "Seven(int)", atFive);
        Assert.DoesNotContain(Prefix + "FourBranches(int)", atSix);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresThresholdParameterWithDefaultAndMinimum()
    {
        var parameter = Assert.Single(s_detector.Parameters);

        Assert.Equal(new DetectorParameter("max_branches", 6, 3), parameter);
    }
}
