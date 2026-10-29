#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// A device function whose barrier depends on an argument is reported where it is called (#446-1); a barrier in a body
/// that the CPU dialects do not split is GSH0037; a divergent barrier is an error (GSH0037) only when thread 0 and
/// thread 1 of a block (block 0 and block 1 for a grid barrier) certainly take different sides, a warning (GSH0083)
/// otherwise.
/// </summary>
[Category(TestCategory.Compiler)]
public class InterproceduralUniformityAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarriers_AcrossCallsAndBySeverity()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/InterproceduralUniformityKernels.cu.cs");

        var error = GSharpDiagnostics.DivergentBarrierProhibited.Id;
        var warning = GSharpDiagnostics.LaunchDependentDivergentBarrier.Id;

        // 35-37: calls whose callee diverges on the argument (37 two calls deep). 68 / 69: a callee that leaves early,
        // followed by a barrier. 82 / 86 / 91: GridStrideLoop, SingleThread and local function bodies, the only errors.
        // 112: a thread-variant branch inside a ForUnroll body. 126-146 and 158-173 (grid barriers): every divergent
        // branch is a warning, whatever its form
        Assert.Equal(
            [
                (warning, 35, 9, 35, 34),
                (warning, 36, 9, 36, 38),
                (warning, 37, 9, 37, 35),
                (warning, 68, 9, 68, 20),
                (warning, 69, 9, 69, 28),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 71, 9, 71, 20),
                (error, 82, 13, 82, 30),
                (error, 86, 13, 86, 30),
                (error, 91, 13, 91, 30),
                (warning, 112, 17, 112, 34),
                (warning, 126, 13, 126, 30),
                (warning, 131, 13, 131, 30),
                (warning, 136, 13, 136, 30),
                (warning, 141, 13, 141, 30),
                (warning, 146, 13, 146, 30),
                (warning, 158, 13, 158, 27),
                (warning, 163, 13, 163, 27),
                (warning, 168, 13, 168, 27),
                (warning, 173, 13, 173, 27)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LaunchDependentDivergentBarrier_IsAWarning()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/InterproceduralUniformityKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var warnings = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.LaunchDependentDivergentBarrier.Id)
            .ToList();
        Assert.Equal(15, warnings.Count);
        Assert.All(warnings, static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
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
