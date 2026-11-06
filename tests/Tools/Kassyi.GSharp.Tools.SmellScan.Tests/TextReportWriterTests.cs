#nullable enable
using System.Text;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class TextReportWriterTests
{
    private const string Config = "Config/all-detectors.yml";

    private static string FindingsText(ScanWorkspace workspace, int parallelism, params string[] files) =>
        Encoding.UTF8.GetString(TextReportWriter.WriteFindings(workspace.Scan(Config, parallelism, files)));

    private static ScanReport Metrics(ScanWorkspace workspace, int top, params string[] files)
    {
        var configText = TestCaseFiles.Read(Config);
        var config = SmellConfigReader.Read(Config, configText);
        return new ScanRunner(workspace.Root, config, 1, top).Run([.. files], Config, configText);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void WriteFindings_OneFile_GroupsLinesUnderThePathWithSizePlaceRuleAndSymbol()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");

        var text = FindingsText(workspace, 1, "src/LongIfChainCases.cs");

        Assert.StartsWith("== src/LongIfChainCases.cs\n", text);
        Assert.Contains(
            "     7 branches 40-46  long-if-chain  Cases.LongIfChainCases.Seven(int)\n",
            text);
        Assert.EndsWith("-- 3 findings in 1 files (1 files measured)\n", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void WriteFindings_PartialType_ListsTheOtherPartsUnderTheFinding()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialAlphaA.cs", "proj/PartialAlphaA.cs");
        workspace.Add("PartialAlphaB.cs", "proj/PartialAlphaB.cs");

        var text = FindingsText(workspace, 1, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        Assert.Contains("    11 lines    3-8  large-type  Cases.Split\n        also proj/PartialAlphaB.cs:3\n", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void WriteFindings_NothingFound_WritesOnlyTheSummary()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("src/Empty.cs", "namespace N;\n");

        var text = FindingsText(workspace, 1, "src/Empty.cs");

        Assert.Equal("-- 0 findings in 0 files (1 files measured)\n", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void WriteFindings_SameFilesWithParallelismOneAndMany_ProducesTheSameBytes()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        var files = new List<string>();
        foreach (var caseFile in new[] { "LongIfChainCases.cs", "LongStringCases.cs", "LargeSwitchCases.cs" })
        {
            workspace.Add(caseFile, "proj/" + caseFile);
            files.Add("proj/" + caseFile);
        }

        Assert.Equal(FindingsText(workspace, 1, [.. files]), FindingsText(workspace, 8, [.. files]));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void WriteMetrics_Top_ListsTheLargestMeasurementsOfEachRuleAndMetricFirstWithTheOnesAboveTheLimitMarked()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");

        var report = Metrics(workspace, 2, "src/LongIfChainCases.cs");
        var text = Encoding.UTF8.GetString(TextReportWriter.WriteMetrics(report));

        // Seven (7 branches) and FourBranches (4) are the two largest; the chain in the else block (4) is cut off by --top 2,
        // and the tie between the two chains of 4 goes to the first in the file
        Assert.Contains("== long-if-chain (branches)\n", text);
        Assert.Contains("     7 branches 40-46  src/LongIfChainCases.cs  Cases.LongIfChainCases.Seven(int)  > 5\n", text);
        Assert.Contains("     4 branches 7-22  src/LongIfChainCases.cs  Cases.LongIfChainCases.FourBranches(int)\n", text);
        Assert.DoesNotContain("IfInsideElseBlock", text);
        Assert.EndsWith("-- 1 files measured\n", text);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_MetricsMode_KeepsMeasurementsBelowTheThresholdThatNormalModeDrops()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("LongIfChainCases.cs", "src/LongIfChainCases.cs");

        var withMetrics = Metrics(workspace, 10, "src/LongIfChainCases.cs");
        var normal = workspace.Scan(Config, 1, "src/LongIfChainCases.cs");

        Assert.Empty(normal.Metrics);
        Assert.Contains(withMetrics.Metrics, static m => m.Measurement.Value == 3 && !m.Exceeds);
        Assert.Equal(normal.Findings.Length, withMetrics.Findings.Length);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Scan_MetricsMode_SumsPartialTypesBeforeRanking()
    {
        using var workspace = new ScanWorkspace();
        workspace.Write("proj/P.csproj", "<Project />");
        workspace.Add("PartialAlphaA.cs", "proj/PartialAlphaA.cs");
        workspace.Add("PartialAlphaB.cs", "proj/PartialAlphaB.cs");

        var report = Metrics(workspace, 10, "proj/PartialAlphaA.cs", "proj/PartialAlphaB.cs");

        var split = new List<RuleMetric>();
        foreach (var metric in report.Metrics)
        {
            if (metric.Measurement.Symbol == "Cases.Split" && metric.Measurement.Metric == "codeLines")
            {
                split.Add(metric);
            }
        }

        var only = Assert.Single(split);
        Assert.Equal(11, only.Measurement.Value);
        Assert.True(only.Exceeds);
    }
}
