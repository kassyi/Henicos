using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// After a thread of a cooperative launch failed, the waiters at the grid barrier leave it at once, and a block that
/// arrives late leaves it on arrival: with the Deterministic barrier no block runs the code after the barrier, whatever
/// the order of arrival; with the Fast barrier no block runs past its next grid barrier. The launch throws the
/// recorded exception.
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
    public void Test_SlowBlockAfterFailure_EveryBlockLeavesTheBarrier()
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

    // [WHY] The promise of a Fast barrier after a failure: the launch throws the recorded exception, no thread waits,
    // the failing block runs nothing after the barrier, and the other blocks run at most up to their next grid
    // barrier. Blocks 1 to 3 wait at the first barrier from the start and block 0 arrives about 1 ms later, after its
    // thread 5 failed, so its arrival may complete the first barrier: the writes between the two barriers of blocks 1
    // to 3 may be there or not. Block 0 never reaches the second barrier, which therefore never completes, so no thread
    // writes after it. Every launch must keep all of this; none depends on the order of arrival.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void FastBarriers_ThreadFailsBeforeTheFirst_NothingRunsPastTheSecond()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Launches = 20;
        long cycles = GpuClock.RateKilohertz();
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierTimingKernels.FailKey;

        for (int launch = 0; launch < Launches; launch++)
        {
            var between = new int[keys.Length];
            var result = new int[keys.Length];
            Array.Fill(between, Untouched);
            Array.Fill(result, Untouched);

            var ex = Assert.Throws<SwitchExpressionException>(() =>
                GridBarrierTimingKernels.LaunchLateFailingBlockTwoFastBarriersKernel(keys, cycles, between, result,
                    gridDimX: Blocks, blockDimX: BlockSize));

            Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
            Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
            Assert.Null(ex.InnerException);
            // Each property is counted on its own, so a failure names every one that broke
            // The failing block runs nothing after the first barrier
            int failingBlockWrites = between.Take(BlockSize).Count(static value => value != Untouched);
            // Between the barriers another block wrote its own value or nothing
            int foreignWrites = Enumerable.Range(BlockSize, between.Length - BlockSize)
                .Count(i => between[i] != Untouched && between[i] != i);
            // No thread runs past the second barrier
            int writesPastSecond = result.Count(static value => value != Untouched);
            Assert.True(failingBlockWrites == 0 && foreignWrites == 0 && writesPastSecond == 0,
                $"launch {launch}: {failingBlockWrites} writes of the failing block after the first barrier, " +
                $"{foreignWrites} wrong writes between the barriers, " +
                $"{writesPastSecond} writes after the second barrier");
        }
    }

    // [WHY] Blocks 1 to 3 wait at the barrier from the start; block 0 arrives about 1 ms later, after its thread 5
    // failed. A Deterministic barrier reads the abort word before it arrives, so block 0 does not arrive, the barrier
    // never completes and the waiting blocks leave through their poll check without writing
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeterministicBarrier_FailingBlockArrivesLast_NoBlockPassesIt()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        long cycles = GpuClock.RateKilohertz();
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierTimingKernels.FailKey;
        var result = new int[keys.Length];
        Array.Fill(result, Untouched);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierTimingKernels.LaunchLateFailingBlockDeterministicKernel(keys, cycles, result, gridDimX: Blocks,
                blockDimX: BlockSize));

        Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }
}
