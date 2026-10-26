using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0077: a dynamic <c>SharedMemory&lt;T&gt;</c> declaration, or a call to a device function that reaches one,
/// that only some threads of a block reach, and a dynamic declaration whose count depends on the thread (#439). The
/// analyzer reports it in every dialect, because the CUDA build of the same kernel is the one that may go wrong.
/// </summary>
[Category(TestCategory.Compiler)]
public class DivergentDynamicSharedMemoryDiagnosticsTests
{
    private const string SadPath = "Diagnostics/DivergentDynamicSharedMemory.cu.cs";

    private const string Explanation =
        ". On CUDA each thread places the dynamic SharedMemory<T> regions of its block in the order it first reaches " +
        "them, with the count it computes, so threads that reach them differently may disagree on where a region is.";

    private const string ReachesDeclaration =
        "Only some threads of the block reach the dynamic SharedMemory<T> declaration";

    private const string ReachesCall = "Only some threads of the block reach the call to";

    private const string CallSuffix = "which reaches a dynamic SharedMemory<T> declaration";

    // Line 29: the declaration under 'if (tid < 16)'. Line 40: the call in a branch of '?:'. Line 43: a call that
    // reaches the declaration through another device function. Line 54: the count 'n + tid'. Line 66: the declaration
    // in a loop whose condition reads Gpu.LaneId(), in a device function. Line 90: a declaration under two
    // thread-variant conditions, reported once. Line 106: the call in an arm of a switch expression on 'tid'.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentDynamicSharedMemory_ReportsGsh0077(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(SadPath, dialect);

        Assert.Equal(
            [
                (Analyzer, 29, 24, 29, 48),
                (Analyzer, 40, 32, 40, 42),
                (Analyzer, 43, 18, 43, 35),
                (Analyzer, 54, 47, 54, 54),
                (Analyzer, 66, 23, 66, 47),
                (Analyzer, 90, 30, 90, 54),
                (Analyzer, 106, 18, 106, 28)
            ],
            diagnostics.Ranges(GSharpDiagnostics.DivergentDynamicSharedMemory.Id));
        Assert.Equal(
            [
                $"{ReachesDeclaration} 'half'{Explanation}",
                $"{ReachesCall} 'Scratch', {CallSuffix}{Explanation}",
                $"{ReachesCall} 'ScratchThrough', {CallSuffix}{Explanation}",
                $"The count of the dynamic SharedMemory<T> declaration 'perThread' depends on the thread{Explanation}",
                $"{ReachesDeclaration} 'row'{Explanation}",
                $"{ReachesDeclaration} 'nested'{Explanation}",
                $"{ReachesCall} 'Scratch', {CallSuffix}{Explanation}"
            ],
            diagnostics
                .Where(static d => d.Id == GSharpDiagnostics.DivergentDynamicSharedMemory.Id)
                .Select(static d => d.Message));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentDynamicSharedMemory_IsAWarning(string dialect)
    {
        var (_, analyzer, _) =
            await KernelDiagnostics.GetDiagnosticsAsync(RoslynTestUtility.ReadTestCase(SadPath), dialect);

        var reported = analyzer
            .Where(static d => d.Id == GSharpDiagnostics.DivergentDynamicSharedMemory.Id)
            .ToList();
        Assert.Equal(7, reported.Count);
        Assert.All(reported, static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    // A uniform condition (n > 0), a uniform count (BlockDimension.X), and a fixed-size declaration and a call that
    // does not reach dynamic shared memory under 'if (tid < 16)'
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_UniformDynamicSharedMemory_ReportsNothing(string dialect)
    {
        var diagnostics =
            await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UniformDynamicSharedMemory.cu.cs", dialect);

        Assert.Empty(diagnostics);
    }
}
