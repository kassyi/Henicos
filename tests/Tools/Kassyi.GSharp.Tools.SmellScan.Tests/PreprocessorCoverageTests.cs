#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>Which conditional branches are reached, and what is reported about the ones that are not.</summary>
public class PreprocessorCoverageTests
{
    private static HashSet<string> Measured(string caseFile)
    {
        var parsed = SyntaxInput.Parse(caseFile, TestCaseFiles.Read(caseFile));
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in parsed.Variants)
        {
            var sink = new MeasurementSink();
            new MethodLengthDetector().Measure(variant, sink);
            foreach (var measurement in sink.Items)
            {
                symbols.Add(measurement.Symbol);
            }
        }

        return symbols;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_BranchThatNeedsTwoSymbolsOn_IsReachedByTheExhaustiveSearch()
    {
        // A && B && !C && !D is reached by neither a single symbol, nor all symbols, nor all but one.
        Assert.Contains("Cases.ExhaustiveOnlyCases.OnlyWithAAndB()", Measured("ExhaustiveOnlyCases.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_NineSymbols_TriesAllButOneSymbol()
    {
        Assert.Contains("Cases.ManySymbols.InAllButLast()", Measured("ManySymbols.cs"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_ExhaustiveLimit_IsEightSymbols()
    {
        Assert.Equal(8, SyntaxInput.MaxExhaustiveSymbols);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Parse_UnreachableBranch_IsListedAsUnmeasuredWithItsLine()
    {
        var parsed = SyntaxInput.Parse("UnreachableBranchCases.cs", TestCaseFiles.Read("UnreachableBranchCases.cs"));

        Assert.Equal([5], TestValues.Ints(parsed.UnmeasuredBranchLines));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Parse_AllBranchesReached_ListsNothing()
    {
        foreach (var name in new[] { "PreprocessorCases.cs", "ManySymbols.cs", "MethodLengthCases.cs", "ExhaustiveOnlyCases.cs" })
        {
            Assert.Empty(TestValues.Ints(SyntaxInput.Parse(name, TestCaseFiles.Read(name)).UnmeasuredBranchLines));
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_UnmeasuredBranch_IsReportedInTheScopeWithoutFailing()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("UnreachableBranchCases.cs", "src/Unreachable.cs");
        workspace.Add("PreprocessorCases.cs", "src/Reachable.cs");

        var report = workspace.Scan("Config/low-thresholds.yml", 1, "src/Unreachable.cs", "src/Reachable.cs");
        using var document = System.Text.Json.JsonDocument.Parse(JsonReportWriter.Write(report));
        var scope = document.RootElement.GetProperty("scope");

        Assert.Equal(1, scope.GetProperty("unmeasuredBranches").GetInt32());
        var files = new List<string>();
        foreach (var file in scope.GetProperty("unmeasuredBranchFiles").EnumerateObject())
        {
            files.Add(file.Name + ":" + file.Value[0].GetInt32());
        }

        Assert.Equal(["src/Unreachable.cs:5"], files);
    }
}
