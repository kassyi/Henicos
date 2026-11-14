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
    public async Task MemoryWrittenSinceLastBarrier_ThreadVariantWrite_IsThreadVariant()
    {
        var diagnostics = await GeneratorTestRunner
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
        // callee. 246 / 249: a callee that some threads leave before its barrier (the call, and the read after it). 265 /
        // 280: a write and a read through a ref local. 295 / 309 / 323 / 338: a local that holds the array, a span or the
        // shared memory. 354: a write before Gpu.SyncGrid(body), read in its first statement. 385: a struct member that
        // writes its receiver's array. 406: a span receiver written by a BCL call in the callee. 421 / 441: an array
        // auto-property, directly and through a call. 459: a write through a ref-returning device function. 501: a callee
        // that writes its ref parameter after its barrier. 517: a callee that only waits at a block barrier, before a grid
        // barrier. 538: an array of a nested struct written by the callee. 553: an increment. 569: a deconstruction.
        // Write, barrier, read (WriteBarrierReadKernel), an input never written (InputKernel), a read after a grid
        // barrier (GridBarrierKernel), a read after a callee that writes and then waits at a barrier
        // (CallThatSyncsKernel), after a reduction that starts with a barrier (ReduceKernel), after a helper that only
        // waits (BarrierHelperKernel), after a callee that writes its ref parameter and then waits
        // (RefWriteThenSyncKernel) and a write and a read separated by a gap of Gpu.SyncGrid(body) (SyncGridGapKernel)
        // are not reported
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
                (warning, 249, 13, 249, 30),
                (warning, 265, 13, 265, 30),
                (warning, 280, 13, 280, 30),
                (warning, 295, 13, 295, 30),
                (warning, 309, 13, 309, 30),
                (warning, 323, 13, 323, 30),
                (warning, 338, 13, 338, 30),
                (warning, 354, 17, 354, 34),
                (warning, 385, 13, 385, 30),
                (warning, 406, 13, 406, 30),
                (warning, 421, 13, 421, 30),
                (warning, 441, 13, 441, 30),
                (warning, 459, 13, 459, 30),
                (warning, 501, 13, 501, 30),
                (warning, 517, 13, 517, 27),
                (warning, 538, 13, 538, 30),
                (warning, 553, 13, 553, 30),
                (warning, 569, 13, 569, 30)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task FlowCapturedValues_WritesThroughCaptures_AreFollowed()
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/BarrierUsage/FlowCaptureWriteKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var actual = diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToList();

        // 27: an element write whose value is a conditional. 40: a with expression. 57: a switch expression. 70: a
        // compound assignment whose value is a conditional. 84: a field set by an object initializer. 98: a property
        // set by a with expression on a local. 129: the control, a plain value. 144: a write through a local that holds
        // one of two arrays, chosen by a conditional. 163: a field of an element written with a conditional value.
        // 209 / 225: an auto-property and an init accessor with a body, set by an object initializer.
        // Nothing at 115, where a barrier separates the write from the read and the initializer is uniform, nor at
        // 177, where a with expression copies an element into a local
        var warning = GSharpDiagnostics.LaunchDependentDivergentBarrier.Id;
        Assert.Equal(
            [
                (warning, 27, 13, 27, 30),
                (warning, 40, 13, 40, 30),
                (warning, 57, 13, 57, 30),
                (warning, 70, 13, 70, 30),
                (warning, 84, 13, 84, 30),
                (warning, 98, 13, 98, 30),
                (warning, 129, 13, 129, 30),
                (warning, 144, 13, 144, 30),
                (warning, 163, 13, 163, 30),
                (warning, 209, 13, 209, 30),
                (warning, 225, 13, 225, 30)
            ],
            actual);
    }
}
