using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A local const whose declaration a goto jumps over, on the managed CPU path.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalConstSkippedByGotoManagedTests : TranspilerTestBase
{
    private const int ThreadCount = 2;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConstSkippedByGoto_ManagedStateMachine_IsReadOnEveryPath()
    {
        var result = new int[ThreadCount];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            LocalConstSkippedByGotoKernels.LaunchConstSkippedByGotoKernel(result, blockDimX: ThreadCount);
        }

        Assert.Equal([3, 4], result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConstSkippedByGoto_ManagedNoBarrier_IsReadOnEveryPath()
    {
        var result = new int[ThreadCount];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            LocalConstSkippedByGotoKernels.LaunchConstSkippedByGotoNoBarrierKernel(result, blockDimX: ThreadCount);
        }

        Assert.Equal([3, 4], result);
    }
}
