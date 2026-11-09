using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// After a thread of a cooperative launch failed, the waiters at the grid barrier leave it at once; a block that is only
/// late still passes the barrier when its arrival completes it, and the launch throws the recorded exception.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public sealed class GridBarrierTimingTests
{
    private const int Untouched = -7;
    private const int Blocks = 4;
    private const int BlockSize = 128;

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SlowBlockAfterFailure_WaitersLeaveAndSlowBlockPassesTheBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Thread 5 of block 0 fails at once; block 3 spends about 40 ms (at the base clock) before the barrier. The
        // waiters of blocks 0 to 2 read the abort word of the launch within a few polls and leave, so their threads write
        // nothing after the barrier; block 3 arrives last, which completes the barrier for it.
        long cycles = 40L * GpuClock.RateKilohertz();
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierTimingKernels.FailKey;
        var result = new int[keys.Length];
        Array.Fill(result, Untouched);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierTimingKernels.LaunchSlowBlockAfterFailKernel(keys, slowBlock: 3, cycles, result,
                gridDimX: Blocks, blockDimX: BlockSize));

        Assert.Null(ex.InnerException);
        Assert.Equal(Untouched, result[5]);
        Assert.Equal(Untouched, result[BlockSize + 7]);
        Assert.Equal(3 * BlockSize + 7, result[3 * BlockSize + 7]);
    }
}
