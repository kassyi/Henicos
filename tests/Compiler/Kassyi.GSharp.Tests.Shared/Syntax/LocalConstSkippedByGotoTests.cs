using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A local const whose declaration a goto jumps over, on the native CPU path (Tests.Cpu) and on CUDA (Tests.Cuda).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class LocalConstSkippedByGotoTests : TranspilerTestBase
{
    private const int ThreadCount = 2;

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConstSkippedByGoto_NoBarrier_IsReadOnEveryPath()
    {
        var result = new int[ThreadCount];

        LocalConstSkippedByGotoKernels.LaunchConstSkippedByGotoNoBarrierKernel(result, blockDimX: ThreadCount);

        Assert.Equal([3, 4], result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConstSkippedByGoto_AfterBarrier_IsReadOnEveryPath()
    {
        var result = new int[ThreadCount];

        LocalConstSkippedByGotoKernels.LaunchConstSkippedByGotoKernel(result, blockDimX: ThreadCount);

        Assert.Equal([3, 4], result);
    }
}
