#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class LargeSwitchDetectorTests
{
    private const string Cases = "LargeSwitchCases.cs";
    private const string Prefix = "Cases.LargeSwitchCases.";
    private static readonly LargeSwitchDetector s_detector = new();

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_SectionWithTwoStatements_CountsSectionsAndReportsTheWholeSwitch()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, Prefix + "StatementWithOneTallSection(int)");

        Assert.Equal("cases", measurement.Metric);
        Assert.Equal(4, measurement.Value);
        Assert.Equal(new SourceRange(116, 9, 124, 10), measurement.Range);
        Assert.Equal(9, measurement.PhysicalLines);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_ExpressionWithABlockLambdaArm_CountsArms()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, Prefix + "ExpressionWithABlockLambdaArm(int)");

        Assert.Equal("cases", measurement.Metric);
        Assert.Equal(5, measurement.Value);
        Assert.Equal(new SourceRange(145, 56, 152, 6), measurement.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_ExpressionWithANestedSwitchArm_IsNotATableButTheSmallInnerSwitchIs()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, Prefix + "ExpressionWithANestedSwitchArm(int,int)");

        Assert.Equal(4, measurement.Value);
        Assert.Equal(new SourceRange(154, 64, 160, 6), measurement.Range);
        Assert.Equal(1, DetectorHarness.Count(s_detector, Cases, Prefix + "ExpressionWithANestedSwitchArm(int,int)"));
    }

    [Theory]
    [InlineData("TableStatement(int)")]
    [InlineData("TableExpression(int)")]
    [InlineData("TableWithOneStatementOnTwoLines(int)")]
    [InlineData("TableWithFallThroughLabelsOnTheirOwnLines(int)")]
    [InlineData("TableWithBreaksAndBlocks(int)")]
    [InlineData("TableExpressionOnTwoLines(int)")]
    [InlineData("OneWorkAndReturnOnOneLine(object,out int)")]
    [InlineData("WorkAndBareReturn(int)")]
    [InlineData("TableEndingInEveryKindOfJump(int)")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_TableOfOneStatementCases_IsNotMeasuredHoweverItIsLaidOut(string method)
    {
        Assert.Equal(0, DetectorHarness.Count(s_detector, Cases, Prefix + method));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_TwoPiecesOfWorkBeforeTheReturn_AreNotATableEntry()
    {
        var measurement = DetectorHarness.Single(s_detector, Cases, Prefix + "TwoStatementsOnOneLine(int,out int)");

        Assert.Equal(3, measurement.Value);
        Assert.Equal(new SourceRange(106, 9, 111, 10), measurement.Range);
    }

    [Theory]
    [InlineData("LocalsAndIfs(int)", 4)]
    [InlineData("OptionParsing(string,int,int,out string)", 3)]
    [InlineData("GotoCaseChain(int)", 3)]
    [InlineData("BreakInTheMiddle(int)", 3)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_SectionsThatDoSeveralThings_AreReportedKeepingTheirJumps(string method, int cases)
    {
        Assert.Equal(cases, DetectorHarness.Single(s_detector, Cases, Prefix + method).Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_IfWithElseInASection_CountsBothBranchesAsStatements()
    {
        Assert.Equal(3, DetectorHarness.Single(s_detector, Cases, Prefix + "IfElseInASection(int)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_ExpressionWithADelegateBodyArm_IsNotATable()
    {
        Assert.Equal(3, DetectorHarness.Single(s_detector, Cases, Prefix + "ExpressionWithADelegateArm(int)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_SwitchOverAnEnum_IsMeasuredLikeAnyOther()
    {
        Assert.Equal(5, DetectorHarness.Single(s_detector, Cases, Prefix + "EnumSwitch(Kind)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_SeveralLabelsOnOneSection_CountAsOneSection()
    {
        Assert.Equal(3, DetectorHarness.Single(s_detector, Cases, Prefix + "SharedLabels(int)").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_SwitchInsideALambda_NamesTheLambda()
    {
        Assert.Equal(3, DetectorHarness.Single(s_detector, Cases, Prefix + "InsideLambda(int).<lambda>").Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_CasesEqualToThreshold_AreNotReportedButOneMoreIs()
    {
        var atFive = DetectorHarness.ExceedingSymbols(s_detector, Cases, 5);
        var atFour = DetectorHarness.ExceedingSymbols(s_detector, Cases, 4);

        Assert.DoesNotContain(Prefix + "EnumSwitch(Kind)", atFive);
        Assert.Contains(Prefix + "EnumSwitch(Kind)", atFour);
        Assert.DoesNotContain(Prefix + "StatementWithOneTallSection(int)", atFour);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresThresholdParameterWithDefaultAndMinimum()
    {
        var parameter = Assert.Single(s_detector.Parameters);

        Assert.Equal(new DetectorParameter("max_cases", 6, 1), parameter);
    }
}
