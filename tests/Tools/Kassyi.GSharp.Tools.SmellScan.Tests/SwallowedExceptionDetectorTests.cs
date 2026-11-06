#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class SwallowedExceptionDetectorTests
{
    private const string Cases = "SwallowedExceptionCases.cs";
    private const string Prefix = "Cases.SwallowedCases.";
    private static readonly SwallowedExceptionDetector s_detector = new();

    private static string? MetricOf(string method)
    {
        var found = new List<Measurement>();
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            if (measurement.Symbol == Prefix + method)
            {
                found.Add(measurement);
            }
        }

        return found.Count == 0 ? null : Assert.Single(found).Metric;
    }

    [Theory]
    [InlineData("EmptyTyped()")]
    [InlineData("EmptyBare()")]
    [InlineData("CommentOnly()")]
    [InlineData("OnlyEmptyStatement()")]
    [InlineData("FilteredAndEmpty(bool)")]
    [InlineData("NestedEmptyBlock()")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_HandlerWithoutStatements_IsAnEmptyCatchWhateverTheTypeOrFilter(string method)
    {
        Assert.Equal("emptyCatch", MetricOf(method));
    }

    [Theory]
    [InlineData("ConsoleOnly()")]
    [InlineData("DebugAndTrace()")]
    [InlineData("LoggerMember()")]
    [InlineData("LogMethodName()")]
    [InlineData("CapitalLoggerReceiver()")]
    [InlineData("ConditionalLog()")]
    [InlineData("ConditionalLogByMethodName()")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_HandlerThatOnlyWritesALogLine_IsALogOnlyCatch(string method)
    {
        Assert.Equal("logOnlyCatch", MetricOf(method));
    }

    [Theory]
    [InlineData("Rethrows()")]
    [InlineData("ReturnsFallback()")]
    [InlineData("AssignsFlag()")]
    [InlineData("CallsOther()")]
    [InlineData("LogsThenAssigns()")]
    [InlineData("NameOnlyStartsWithLog()")]
    [InlineData("ConditionalOtherCall()")]
    [InlineData("OnlyReturns()")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_HandlerThatDoesSomethingElse_IsNotReported(string method)
    {
        Assert.Null(MetricOf(method));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_Catch_ReportsTheWholeClauseFromCatchToTheClosingBrace()
    {
        var typed = DetectorHarness.Single(s_detector, Cases, Prefix + "EmptyTyped()");
        var multiLine = DetectorHarness.Single(s_detector, Cases, Prefix + "EmptyBare()");
        var filtered = DetectorHarness.Single(s_detector, Cases, Prefix + "FilteredAndEmpty(bool)");

        Assert.Equal(new SourceRange(28, 9, 28, 46), typed.Range);
        Assert.Equal(new SourceRange(37, 9, 39, 10), multiLine.Range);
        Assert.Equal(3, multiLine.PhysicalLines);
        Assert.Equal(new SourceRange(60, 9, 60, 49), filtered.Range);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_CatchInsideALambda_NamesTheLambda()
    {
        Assert.Equal(
            "emptyCatch",
            DetectorHarness.Single(s_detector, Cases, Prefix + "InsideLambda().<lambda>").Metric);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_AnyMeasuredCatch_IsAFindingWithThresholdZero()
    {
        var findings = DetectorHarness.Judge(s_detector, Cases);

        // 6 empty and 7 log-only methods of the file, and the one in the lambda
        Assert.Equal(14, findings.Count);
        Assert.All(findings, static finding => Assert.Equal(0, finding.Threshold));
        Assert.Empty(s_detector.Parameters);
    }
}
