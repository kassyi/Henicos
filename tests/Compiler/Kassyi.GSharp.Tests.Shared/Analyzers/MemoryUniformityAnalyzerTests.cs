#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// C9 (noclobber): an element of shared memory read with a uniform index is uniform only when no path from the last
/// barrier writes the same memory; a block barrier makes it the same within a block, a grid barrier within the grid,
/// and shared memory is never the same across blocks.
/// </summary>
[Category(TestCategory.Compiler)]
public class MemoryUniformityAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MemoryWrittenSinceTheLastBarrier_IsThreadVariant()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/MemoryUniformityKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var actual = diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToList();

        // 45: written after the barrier. 60: a call that writes the array. 66: an atomic. 100: after a block barrier only,
        // for a grid barrier. 130: shared memory after a grid barrier. 171: an array of a struct argument written by the
        // callee. 246 / 249: a callee that some threads leave before its barrier (the call, and the read after it). Write, barrier, read (WriteBarrierReadKernel), an
        // input never written (InputKernel), a read after a grid barrier (GridBarrierKernel) a read after a callee that
        // writes and then waits at a barrier (CallThatSyncsKernel), after a reduction that starts with a barrier
        // (ReduceKernel) and after a helper that only waits (BarrierHelperKernel) are not reported
        var warning = GSharpDiagnostics.LaunchDependentDivergentBarrier.Id;
        Assert.Equal(
            [
                (warning, 45, 13, 45, 30),
                (warning, 60, 13, 60, 30),
                (warning, 66, 13, 66, 30),
                (warning, 100, 13, 100, 27),
                (warning, 130, 13, 130, 27),
                (warning, 171, 13, 171, 30),
                (warning, 246, 9, 246, 32),
                (warning, 249, 13, 249, 30)
            ],
            actual);
    }
}
