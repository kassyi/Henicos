using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// After a thread of a cooperative launch failed, the waiters at the grid barrier leave it at once, and a block that
/// arrives late leaves it on arrival: with the Deterministic barrier no block runs the code after the barrier, whatever
/// the order of arrival, and the launch throws the recorded exception. With the Fast barrier a failing block still
/// arrives, so the blocks already waiting pass it.
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

    // [WHY] Blocks 1 to 3 wait at the barrier from the start; block 0 arrives about 1 ms later, after its thread 5
    // failed. A Fast barrier lets block 0 arrive before it reads the abort word, so its arrival completes the barrier
    // and the waiting blocks run the code after it. A waiter that reaches its 1024-poll check between the failure and
    // that arrival leaves instead, so one launch may lose a block: the test allows a few launches and needs one in
    // which every waiting block wrote. A Deterministic barrier never writes (the test below).
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FastBarrier_FailingBlockArrivesLast_WaitingBlocksPassIt()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Launches = 3;
        long cycles = GpuClock.RateKilohertz();
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierTimingKernels.FailKey;

        bool everyWaiterWrote = false;
        for (int launch = 0; launch < Launches && !everyWaiterWrote; launch++)
        {
            var result = new int[keys.Length];
            Array.Fill(result, Untouched);

            var ex = Assert.Throws<SwitchExpressionException>(() =>
                GridBarrierTimingKernels.LaunchLateFailingBlockFastKernel(keys, cycles, result, gridDimX: Blocks,
                    blockDimX: BlockSize));

            Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
            // Block 0 saw the failure after its arrival and left without writing
            Assert.All(result.Take(BlockSize), static value => Assert.Equal(Untouched, value));
            everyWaiterWrote = Enumerable.Range(BlockSize, result.Length - BlockSize).All(i => result[i] == i);
        }

        Assert.True(everyWaiterWrote, $"no launch of {Launches} let every waiting block pass the Fast barrier");
    }

    // [WHY] The same launch with a Deterministic barrier: block 0 reads the abort word before it arrives and does not
    // arrive, so the barrier never completes and the waiting blocks leave through their poll check without writing
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
