#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

public class SyntaxInputTests
{
    private static readonly string[] s_preprocessorMethods =
    [
        "Cases.PreprocessorCases.BeforeDirectives()",
        "Cases.PreprocessorCases.InAlpha()",
        "Cases.PreprocessorCases.InBetaAndGamma()",
        "Cases.PreprocessorCases.InElse()",
        "Cases.PreprocessorCases.InDelta()",
        "Cases.PreprocessorCases.AfterDirectives()",
    ];

    private static HashSet<string> MeasuredAcrossVariants(string caseFile)
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
    [Scenario(TestScenario.HappyPath)]
    public void Parse_FileWithoutDirectives_HasOneVariant()
    {
        var parsed = SyntaxInput.Parse("MethodLengthCases.cs", TestCaseFiles.Read("MethodLengthCases.cs"));

        Assert.Single(parsed.Variants);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Parse_ConditionalFile_MeasuresTheCodeOfEveryBranchAtLeastOnce()
    {
        var measured = MeasuredAcrossVariants("PreprocessorCases.cs");

        foreach (var method in s_preprocessorMethods)
        {
            Assert.Contains(method, measured);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_ConditionalFile_KeepsOnlyTheSymbolSetsThatActivateANewBranch()
    {
        // Five symbols have 31 non-empty subsets. The empty set, ALPHA, DELTA, DELTA+EPSILON and BETA+GAMMA are enough.
        var parsed = SyntaxInput.Parse("PreprocessorCases.cs", TestCaseFiles.Read("PreprocessorCases.cs"));

        Assert.Equal(5, parsed.Variants.Length);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Parse_FileWithMoreSymbolsThanTheExhaustiveLimit_StillMeasuresEveryBranch()
    {
        var measured = MeasuredAcrossVariants("ManySymbols.cs");

        for (var i = 1; i <= 9; i++)
        {
            Assert.Contains($"Cases.ManySymbols.InS{i}()", measured);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Scan_ConditionalFile_ReportsEachMethodOnceWithItsLargestMeasurement()
    {
        using var workspace = new ScanWorkspace();
        workspace.Add("PreprocessorCases.cs", "src/PreprocessorCases.cs");

        var report = workspace.Scan("Config/low-thresholds.yml", 2, "src/PreprocessorCases.cs");

        var bySymbol = new Dictionary<string, Finding>(StringComparer.Ordinal);
        foreach (var finding in report.Findings)
        {
            if (finding.RuleId == "long-method")
            {
                // Add throws when a symbol appears twice, which would mean the variants were not merged.
                bySymbol.Add(finding.Measurement.Symbol, finding);
            }
        }

        foreach (var method in s_preprocessorMethods)
        {
            Assert.Contains(method, bySymbol.Keys);
        }

        // InDelta has 4 code lines, and 5 when EPSILON is defined as well; the larger one is reported.
        Assert.Equal(5, bySymbol["Cases.PreprocessorCases.InDelta()"].Measurement.Value);
        Assert.Equal(4, bySymbol["Cases.PreprocessorCases.InAlpha()"].Measurement.Value);
    }
}
