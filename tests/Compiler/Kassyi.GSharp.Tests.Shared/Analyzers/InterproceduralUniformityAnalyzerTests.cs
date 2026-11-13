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
    public async Task DivergentBarriers_InterproceduralCalls_ReportsExpectedSeveritiesAndSpans()
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
    public async Task LaunchDependentDivergentBarrier_BlockCondition_ReportsWarning()
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/BarrierUsage/InterproceduralUniformityKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var warnings = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.LaunchDependentDivergentBarrier.Id)
            .ToList();
        Assert.Equal(15, warnings.Count);
        Assert.All(warnings, static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task LocalFunctionResults_BodyExpressions_FollowBodyExpressions()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/LocalFunctionVarianceKernels.cu.cs");

        var exit = GSharpDiagnostics.BarrierAfterDivergentExit.Id;

        // 15 / 33 / 52: a lane that a local function returns, or leaves in an out or ref argument. 75: a constant that
        // it returns under a thread-variant branch. 90: a thread-variant local it reads. 104: a thread-variant argument
        // it returns (its barrier at 106, as later barriers follow). Each is reported as the control at 134 that reads
        // the lane directly; nothing at 109 and 120, where it returns or copies a uniform argument
        Assert.Equal(
            [
                (exit, 15, 13, 15, 22),
                (exit, 33, 13, 33, 22),
                (exit, 52, 13, 52, 22),
                (exit, 75, 13, 75, 24),
                (exit, 90, 13, 90, 24),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 106, 13, 106, 30),
                (exit, 134, 13, 134, 22)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task PropertyResults_GettersWithBodies_FollowTheGetterBody()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/PropertyVarianceKernels.cu.cs");

        var warning = GSharpDiagnostics.LaunchDependentDivergentBarrier.Id;

        // 37: an expression-bodied getter that returns the lane. 51: a getter with a block body. 65: an indexer
        // getter that returns the lane for one index. 79: a struct member without [GpuDeviceFunction]. 110: a getter
        // that reads a field of a thread-variant receiver. 124: the control, the lane read directly. 148: a
        // [GpuDeviceFunction] member of a thread-variant struct value. Nothing at 94, where the same getter reads a
        // field of a uniform receiver
        Assert.Equal(
            [
                (warning, 37, 13, 37, 30),
                (warning, 51, 13, 51, 30),
                (warning, 65, 13, 65, 30),
                (warning, 79, 13, 79, 30),
                (warning, 110, 13, 110, 30),
                (warning, 124, 13, 124, 30),
                (warning, 148, 13, 148, 30)
            ],
            actual);
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath)
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer(relativeTestCasePath, s_analyzer).GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }
}
