#if GSHARP_CPU
using Kassyi.GSharp.Runtime.Aot;
#endif

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class PartialMethodTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PartialKernelAndDeviceFunction_RunTheirImplementingBodies() =>
        OnEveryPath(static () =>
        {
            int[] data = new int[4];

            PartialMethodKernels.LaunchPartialFillKernel(data, blockDimX: 4);

            Assert.Equal([7, 8, 9, 10], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartialDeviceFunctionWithBarrier_SuspendsTheCaller() =>
        OnEveryPath(static () =>
        {
            int[] shared = new int[4];
            int[] result = new int[4];

            PartialMethodKernels.LaunchPartialBarrierKernel(shared, result, blockDimX: 4);

            // Every thread writes shared[i] = i + 1 before any reads its neighbour
            Assert.Equal([2, 3, 4, 1], result);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartialDeviceFunctionWithoutImplementation_RemovesTheCallAndItsArguments() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            PartialMethodKernels.LaunchUnimplementedPartialKernel(data, blockDimX: 2);

            // Bump (+100) is an argument of the removed call
            Assert.Equal([11, 21], data);
        });

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartialDeviceFunctionWithoutImplementation_RemovesTheCallInExpressionBodies() =>
        OnEveryPath(static () =>
        {
            int[] data = [10, 20];

            PartialMethodKernels.LaunchUnimplementedPartialInExpressionBodiesKernel(data, blockDimX: 2);

            // Two loop iterations and +10; no Bump (+100) of a lambda, a local function or an incrementor runs
            Assert.Equal([22, 32], data);
        });

    // The native build of the dialect, and on the CPU the managed path as well
    private static void OnEveryPath(Action check)
    {
        check();
#if GSHARP_CPU
        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            check();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
#endif
    }
}
