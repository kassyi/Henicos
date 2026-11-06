#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class ParameterCountDetectorTests
{
    private const string Cases = "ParameterCountCases.cs";
    private static readonly ParameterCountDetector s_detector = new();

    private static List<string> MeasuredSymbols()
    {
        var symbols = new List<string>();
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            symbols.Add(measurement.Symbol);
        }

        return symbols;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_Method_ReportsParameterCountAndRange()
    {
        var measurement = DetectorHarness.Single(
            s_detector, Cases, "Cases.ParameterCountCases.Six(int,int,int,int,int,int)");

        Assert.Equal("parameters", measurement.Metric);
        Assert.Equal(6, measurement.Value);
        Assert.Equal(new SourceRange(31, 5, 33, 6), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_ParameterCountEqualToThreshold_IsNotReportedButOneMoreIs()
    {
        var five = "Cases.ParameterCountCases.Five(int,int,int,int,int)";
        var six = "Cases.ParameterCountCases.Six(int,int,int,int,int,int)";

        var atFive = DetectorHarness.ExceedingSymbols(s_detector, Cases, 5);
        Assert.DoesNotContain(five, atFive);
        Assert.Contains(six, atFive);

        var atFour = DetectorHarness.ExceedingSymbols(s_detector, Cases, 4);
        Assert.Contains(five, atFour);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_ConstructorOperatorAndExpressionBody_AreMeasured()
    {
        Assert.Equal(3, DetectorHarness.Single(s_detector, Cases, "Cases.ParameterCountCases.ParameterCountCases(int,int,int)").Value);
        Assert.Equal(
            2,
            DetectorHarness.Single(
                s_detector,
                Cases,
                "Cases.ParameterCountCases.operator +(ParameterCountCases,ParameterCountCases)").Value);
        Assert.Equal(
            6,
            DetectorHarness.Single(
                s_detector,
                Cases,
                "Cases.ParameterCountCases.ExpressionBodied(int,int,int,int,int,int)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_OverrideExplicitImplementationAndBodylessDeclarations_AreNotMeasured()
    {
        var symbols = MeasuredSymbols();

        Assert.DoesNotContain("Cases.ParameterCountCases.Resize(int,int,int,int,int)", symbols);
        Assert.DoesNotContain("Cases.ParameterCountCases.Move(int,int,int,int,int)", symbols);
        Assert.DoesNotContain("Cases.ParameterCountCases.IShape.Draw(int,int,int,int,int)", symbols);
        Assert.DoesNotContain("Cases.IShape.Draw(int,int,int,int,int)", symbols);
        Assert.DoesNotContain("Cases.ShapeBase.Resize(int,int,int,int,int)", symbols);
        Assert.Contains("Cases.ShapeBase.Move(int,int,int,int,int)", symbols);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_PositionalRecordAndRecordStruct_AreNotMeasured()
    {
        foreach (var measurement in DetectorHarness.Measure(s_detector, "PrimaryConstructorCases.cs"))
        {
            Assert.DoesNotContain("PrimaryRecord", measurement.Symbol);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_ClassAndStructPrimaryConstructors_AreMeasuredOverTheParameterList()
    {
        var cls = DetectorHarness.Single(
            s_detector, "PrimaryConstructorCases.cs", "Cases.PrimaryClass.PrimaryClass(int,int,int,int)");
        var strct = DetectorHarness.Single(
            s_detector, "PrimaryConstructorCases.cs", "Cases.PrimaryStruct.PrimaryStruct(int,int,int,int)");
        var small = DetectorHarness.Single(
            s_detector, "PrimaryConstructorCases.cs", "Cases.SmallPrimary.SmallPrimary(int)");

        Assert.Equal(4, cls.Value);
        Assert.Equal(4, strct.Value);
        Assert.Equal(1, small.Value);

        // The range covers the declaration up to the end of the parameter list, not the whole body.
        Assert.Equal(new SourceRange(3, 1, 3, 54), cls.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_LocalFunctionAndExtensionThis_AreCounted()
    {
        var local = DetectorHarness.Single(
            s_detector, Cases, "Cases.ParameterCountCases.WithLocal().Local(int,int,int,int,int,int)");
        var extension = DetectorHarness.Single(
            s_detector, Cases, "Cases.ParameterCountExtensions.Extend(this string,int,int,int,int,int)");

        Assert.Equal(6, local.Value);
        Assert.Equal(6, extension.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_ParameterModifiers_AppearInTheSymbolAndEachCountsOnce()
    {
        var measurement = DetectorHarness.Single(
            s_detector, Cases, "Cases.ParameterCountCases.Modifiers(ref int,out int,in int,params int[])");

        Assert.Equal(4, measurement.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresThresholdParameterWithDefaultAndMinimum()
    {
        var parameter = Assert.Single(s_detector.Parameters);

        Assert.Equal("max_parameters", parameter.Name);
        Assert.Equal(7, parameter.Default);
        Assert.Equal(1, parameter.Min);
    }
}
