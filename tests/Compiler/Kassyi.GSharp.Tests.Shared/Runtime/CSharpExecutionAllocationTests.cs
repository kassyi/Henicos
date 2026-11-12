#nullable enable
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Verifies that the C# execution branch of a launcher costs nothing on the other paths: the default launch of a
/// value-returning kernel allocates its result array and nothing else more than a void kernel with the same parameters.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CSharpExecutionAllocationTests : TranspilerTestBase
{
    private const uint Threads = 4;

    private static object? s_sink;

    // [WHY] A lambda that captures a local of the whole launcher makes C# allocate its closure when the launcher
    // starts, on every path; the branch must capture only its own locals
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Launch_DefaultValueKernel_AllocatesOnlyTheResultArrayBeyondAVoidKernel()
    {
        Skip.If(IsCudaDialect, "A CUDA launch allocates in the driver bindings; the CPU launchers are measured.");
        for (var i = 0; i < 3; i++)
        {
            LaunchValueKernel();
            LaunchVoidKernel();
        }

        var array = Measure(static () => s_sink = new int[Threads]);
        var value = Measure(LaunchValueKernel);
        var none = Measure(LaunchVoidKernel);

        Assert.Equal(none + array, value);
    }

    private static void LaunchValueKernel() =>
        s_sink = CSharpExecutionKernels.LaunchReturnScaleKernel(1, blockDimX: Threads);

    private static void LaunchVoidKernel() => CSharpExecutionKernels.LaunchIgnoreScaleKernel(1, blockDimX: Threads);

    // The least of several launches, the steady cost: in a full run one launch once allocated 7 KB more for work that
    // the tests running beside it caused
    private static long Measure(Action launch)
    {
        Assert.Equal(KernelExecution.Default, GpuExecution.Current);
        var least = long.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            launch();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }
}
