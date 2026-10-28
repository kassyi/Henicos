#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Shapes that the barrier uniformity analysis once missed or misjudged: a count that a divergent loop leaves behind,
/// a field of a struct local.
/// </summary>
[Category(TestCategory.Compiler)]
public class UniformityReviewAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ReviewedShapes_AreReported()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/UniformityReviewKernels.cu.cs");

        // 27 / 46: a value counted in a loop whose exit test diverges. 61: a field of a struct local. All three depend on
        // run-time values. 75 / 80: a narrowing conversion that every thread takes (not certain). 85: one that wraps
        // to different sides for thread 0 and thread 1. 108: the thread index passed through a local. 120: a barrier
        // in a GridStrideLoop body. 131-141: divisions that overflow (C# throws), which give no constant (and no analyzer
        // exception, which the test would see as AD0001)
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 27, 13, 27, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 46, 13, 46, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 61, 13, 61, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 75, 13, 75, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 80, 13, 80, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 85, 13, 85, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 108, 9, 108, 24),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 120, 13, 120, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 131, 13, 131, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 136, 13, 136, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 141, 13, 141, 30)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_BarrierInUnsplitBody_SaysWhy()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/UniformityReviewKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var unsplit = diagnostics.Single(static d => d.Location.GetLineSpan().StartLinePosition.Line == 119);
        Assert.Equal(GSharpDiagnostics.DivergentBarrierProhibited.Id, unsplit.Id);
        Assert.Contains("cannot be called in a lambda or local function", unsplit.GetMessage(), StringComparison.Ordinal);
        var divergent = diagnostics.Single(static d => d.Location.GetLineSpan().StartLinePosition.Line == 84);
        Assert.Contains("conditional branch", divergent.GetMessage(), StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath)
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(relativeTestCasePath, s_analyzer).GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }
}
