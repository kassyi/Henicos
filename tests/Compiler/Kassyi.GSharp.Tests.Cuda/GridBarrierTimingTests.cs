using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// After a thread of a cooperative launch failed, the waiters at the grid barrier leave it at once, and a block that
/// arrives late leaves it on arrival: no block runs the code after the barrier, whatever the order of arrival, and the
/// launch throws the recorded exception.
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
    public void Launch_SlowBlockArrivingAfterFailure_AbortsAllBlocksLeavingResultUntouched()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Thread 5 of block 0 fails at once; block 3 spends about 40 ms (at the base clock) before the barrier. The
        // waiters of blocks 0 to 2 read the abort word of the launch within a few polls and leave; block 3 reads it
        // when it arrives, last, and leaves as well although its arrival completes the barrier.
        long cycles = 40L * GpuClock.RateKilohertz();
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierTimingKernels.FailKey;
        var result = new int[keys.Length];
        Array.Fill(result, Untouched);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierTimingKernels.LaunchSlowBlockAfterFailKernel(keys, slowBlock: 3, cycles, result,
                gridDimX: Blocks, blockDimX: BlockSize));

        Assert.Null(ex.InnerException);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }
}
