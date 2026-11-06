#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class NestingDepthDetectorTests
{
    private const string Cases = "NestingDepthCases.cs";
    private static readonly NestingDepthDetector s_detector = new();

    private static int DepthOf(string symbol) => DetectorHarness.Single(s_detector, Cases, symbol).Value;

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_FourDeepMethod_ReportsDepthAndTheDeepestStatement()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, "Cases.NestingDepthCases.FourDeep()");

        Assert.Equal("depth", measurement.Metric);
        Assert.Equal(4, measurement.Value);

        // The deepest statement is the try block, from "try" to the closing brace of "finally".
        Assert.Equal(new SourceRange(35, 21, 41, 22), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_DepthEqualToThreshold_IsNotReportedButOneMoreIs()
    {
        var three = "Cases.NestingDepthCases.ThreeDeep()";
        var four = "Cases.NestingDepthCases.FourDeep()";

        var atThree = DetectorHarness.ExceedingSymbols(s_detector, Cases, 3);
        Assert.DoesNotContain(three, atThree);
        Assert.Contains(four, atThree);

        Assert.Contains(three, DetectorHarness.ExceedingSymbols(s_detector, Cases, 2));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_ElseIfChain_DoesNotAddLevels()
    {
        Assert.Equal(1, DepthOf("Cases.NestingDepthCases.ElseIfChain(int)"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_SwitchForAndDo_AreNestingStatements()
    {
        Assert.Equal(3, DepthOf("Cases.NestingDepthCases.SwitchTwoLoops(int)"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_UsingAndLock_DoNotAddLevels()
    {
        Assert.Equal(3, DepthOf("Cases.NestingDepthCases.UsingAndLockDoNotCount()"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_Lambda_StartsAgainAtZeroAndIsMeasuredOnItsOwn()
    {
        Assert.Equal(2, DepthOf("Cases.NestingDepthCases.LambdaStartsAgain()"));
        Assert.Equal(2, DepthOf("Cases.NestingDepthCases.LambdaStartsAgain().<lambda>"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_LocalFunction_StartsAgainAtZeroAndIsMeasuredOnItsOwn()
    {
        Assert.Equal(1, DepthOf("Cases.NestingDepthCases.LocalFunctionStartsAgain()"));
        Assert.Equal(2, DepthOf("Cases.NestingDepthCases.LocalFunctionStartsAgain().Local()"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_TwoSpotsAtTheSameDepth_ReportsTheFirstOne()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, "Cases.NestingDepthCases.TwoSpotsAtTheSameDepth()");

        Assert.Equal(4, measurement.Value);
        Assert.Equal(149, measurement.Range.StartLine);
        Assert.Equal(21, measurement.Range.StartColumn);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_PropertyAccessor_IsAUnit()
    {
        Assert.Equal(4, DepthOf("Cases.NestingDepthCases.Property.get"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_MethodWithoutNestingStatements_ProducesNoMeasurement()
    {
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            Assert.NotEqual("Cases.NestingDepthCases.Check()", measurement.Symbol);
            Assert.NotEqual("Cases.NestingDepthCases.Work()", measurement.Symbol);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresThresholdParameterWithDefaultAndMinimum()
    {
        var parameter = Assert.Single(s_detector.Parameters);

        Assert.Equal("max_depth", parameter.Name);
        Assert.Equal(3, parameter.Default);
        Assert.Equal(1, parameter.Min);
    }
}
