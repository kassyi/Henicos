#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class MethodLengthDetectorTests
{
    private const string Cases = "MethodLengthCases.cs";
    private static readonly MethodLengthDetector s_detector = new();

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_SimpleMethod_ReportsCodeLinesPhysicalLinesAndExactRange()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, "Cases.MethodLengthCases.FourCodeLines()");

        Assert.Equal("codeLines", measurement.Metric);
        Assert.Equal(4, measurement.Value);
        Assert.Equal(4, measurement.PhysicalLines);
        Assert.Equal(new SourceRange(5, 5, 8, 6), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_ValueEqualToThreshold_IsNotReportedButOneMoreIs()
    {
        // BlankAndCommentLinesAreFree has 5 code lines. At a threshold of 5 it passes; at 4 it is reported.
        const string symbol = "Cases.MethodLengthCases.BlankAndCommentLinesAreFree()";

        Assert.DoesNotContain(symbol, DetectorHarness.ExceedingSymbols(s_detector, Cases, 5));
        Assert.Contains(symbol, DetectorHarness.ExceedingSymbols(s_detector, Cases, 4));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_BlankLinesAndComments_AreNotCountedButPhysicalLinesAreKept()
    {
        var measurement = DetectorHarness.Single(
            s_detector, Cases, "Cases.MethodLengthCases.BlankAndCommentLinesAreFree()");

        // 10 physical lines: signature, braces, two statements and a block comment that do not add code lines.
        Assert.Equal(5, measurement.Value);
        Assert.Equal(10, measurement.PhysicalLines);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_ExpressionBodiedMethod_IsMeasuredToTheSemicolon()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, "Cases.MethodLengthCases.ExpressionBody()");

        Assert.Equal(3, measurement.Value);
        Assert.Equal(new SourceRange(21, 5, 23, 16), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_MultiLineVerbatimString_CountsEveryLineOfTheToken()
    {
        var measurement = DetectorHarness.Single(
            s_detector, Cases, "Cases.MethodLengthCases.MultiLineVerbatimString()");

        Assert.Equal(6, measurement.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_LocalFunctionAndLambda_AreIncludedInTheMemberAndTheLocalFunctionIsAlsoMeasured()
    {
        var outer = DetectorHarness.Single(s_detector, Cases, "Cases.MethodLengthCases.Outer()");
        var inner = DetectorHarness.Single(s_detector, Cases, "Cases.MethodLengthCases.Outer().Inner()");

        Assert.Equal(12, outer.Value);
        Assert.Equal(4, inner.Value);
        Assert.Equal(new SourceRange(35, 9, 38, 10), inner.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_PropertyConstructorAndOperator_AreMeasured()
    {
        Assert.Equal(5, DetectorHarness.Single(s_detector, Cases, "Cases.MethodLengthCases.Property").Value);
        Assert.Equal(4, DetectorHarness.Single(s_detector, Cases, "Cases.MethodLengthCases.MethodLengthCases()").Value);
        Assert.Equal(
            4,
            DetectorHarness.Single(
                s_detector,
                Cases,
                "Cases.MethodLengthCases.operator +(MethodLengthCases,MethodLengthCases)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_DeclarationsWithoutBody_AreNotMeasured()
    {
        var symbols = new List<string>();
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            symbols.Add(measurement.Symbol);
        }

        Assert.DoesNotContain("Cases.IMethodLengthCases.NoBody(int)", symbols);
        Assert.DoesNotContain("Cases.AbstractMethodLengthCases.NoBody()", symbols);
        Assert.Contains("Cases.MethodLengthCases.FourCodeLines()", symbols);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresThresholdParameterWithDefaultAndMinimum()
    {
        var parameter = Assert.Single(s_detector.Parameters);

        Assert.Equal("max_code_lines", parameter.Name);
        Assert.Equal(60, parameter.Default);
        Assert.Equal(1, parameter.Min);
    }
}
