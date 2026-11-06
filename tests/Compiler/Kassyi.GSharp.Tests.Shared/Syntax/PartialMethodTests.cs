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
    public void Test_PartialKernelAndDeviceFunction_RunTheirImplementingBodies()
    {
        Check();
#if GSHARP_CPU
        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            Check();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
#endif
    }

    private static void Check()
    {
        int[] data = new int[4];

        PartialMethodKernels.LaunchPartialFillKernel(data, blockDimX: 4);

        Assert.Equal([7, 8, 9, 10], data);
    }
}
