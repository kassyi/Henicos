#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class LongStringLiteralDetectorTests
{
    private const string Cases = "LongStringCases.cs";
    private static readonly LongStringLiteralDetector s_detector = new();

    private static Measurement OnLine(int line)
    {
        var found = new List<Measurement>();
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            if (measurement.Range.StartLine == line)
            {
                found.Add(measurement);
            }
        }

        return Assert.Single(found);
    }

    private static List<int> MeasuredLines()
    {
        var lines = new List<int>();
        foreach (var measurement in DetectorHarness.Measure(s_detector, Cases))
        {
            lines.Add(measurement.Range.StartLine);
        }

        return lines;
    }

    private static List<int> FindingLines(params (string Name, int Value)[] values)
    {
        var lines = new List<int>();
        foreach (var finding in DetectorHarness.Judge(s_detector, Cases, values))
        {
            lines.Add(finding.Measurement.Range.StartLine);
        }

        return lines;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_LongLineWithAStringLiteral_ReportsItsLengthFromTheFirstNonBlankColumn()
    {
        var measurement = OnLine(6);

        Assert.Equal("columns", measurement.Metric);
        Assert.Equal(121, measurement.Value);
        Assert.Equal(new SourceRange(6, 5, 6, 122), measurement.Range);
        Assert.Equal("Cases.LongStringCases", measurement.Symbol);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_LineOfExactlyTheLimit_IsNotReportedButOneMoreIs()
    {
        var atDefault = FindingLines();

        Assert.Equal(120, OnLine(5).Value);
        Assert.DoesNotContain(5, atDefault);
        Assert.Contains(6, atDefault);
        Assert.Contains(5, FindingLines((LongStringLiteralDetector.MaxColumns, 119)));
        Assert.DoesNotContain(6, FindingLines((LongStringLiteralDetector.MaxColumns, 121)));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_LineAtTheFloor_IsNotMeasuredButOneMoreIs()
    {
        var lines = MeasuredLines();

        Assert.DoesNotContain(22, lines);
        Assert.Contains(23, lines);
        Assert.Equal(81, OnLine(23).Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Measure_LongLinesWithoutAStringToken_AreNotMeasured()
    {
        var lines = MeasuredLines();

        // 7: no string at all; 8: a comment that mentions a quote; 18: a character literal holding a quote
        Assert.DoesNotContain(7, lines);
        Assert.DoesNotContain(8, lines);
        Assert.DoesNotContain(18, lines);
        Assert.DoesNotContain(21, lines);
    }

    [Theory]
    [InlineData(10, 130, "columns")]
    [InlineData(15, 124, "columns")]
    [InlineData(16, 123, "columns")]
    [InlineData(17, 122, "columns")]
    [InlineData(19, 147, "columns")]
    [InlineData(13, 148, "rawContentColumns")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Measure_EveryKindOfStringLiteral_IsFoundOnceForTheLine(int line, int columns, string metric)
    {
        var measurement = OnLine(line);

        Assert.Equal(columns, measurement.Value);
        Assert.Equal(metric, measurement.Metric);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_LineWithCharactersOutsideTheBasicPlane_CountsCodePoints()
    {
        Assert.Equal(121, OnLine(20).Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Judge_MultiLineRawStringContent_IsLeftOutUnlessTheParameterAsksForIt()
    {
        var byDefault = FindingLines();
        var included = FindingLines((LongStringLiteralDetector.IncludeRawContent, 1));

        Assert.DoesNotContain(13, byDefault);
        Assert.Contains(13, included);
        Assert.Equal([6, 10, 15, 16, 17, 19, 20], byDefault);
    }

    [Theory]
    [InlineData(5, 148)]
    [InlineData(10, 127)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Measure_LineWithARawStringAndAnOrdinaryOne_IsAnOrdinaryLineWhicheverComesFirst(int line, int columns)
    {
        var found = new List<Measurement>();
        foreach (var measurement in DetectorHarness.Measure(s_detector, "LongStringMixedCases.cs"))
        {
            if (measurement.Range.StartLine == line)
            {
                found.Add(measurement);
            }
        }

        var only = Assert.Single(found);
        Assert.Equal("columns", only.Metric);
        Assert.Equal(columns, only.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Detector_DeclaresTheLimitWithTheFloorAsMinimum()
    {
        Assert.Equal(new DetectorParameter("max_columns", 120, 80), s_detector.Parameters[0]);
        Assert.Equal(new DetectorParameter("include_multiline_raw_content", 0, 0), s_detector.Parameters[1]);
    }
}
