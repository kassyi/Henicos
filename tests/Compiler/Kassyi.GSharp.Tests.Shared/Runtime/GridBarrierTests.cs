using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// The CUDA grid barrier (<c>Gpu.SyncGrid</c>): the threads of a block that have not exited arrive for it, so threads,
/// warps or thread (0,0,0) that failed and exited before it do not stop the others; a block whose threads all exited
/// makes the waiters trap instead of hang; repeated barriers over the largest cooperative grid stay exact; and the writes
/// before a barrier are visible to every block after it.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
#if GSHARP_CUDA
// A device error goes to the one error slot of the process, which a test running beside this one could read first
[Collection(TestCollections.DeviceExclusive)]
#endif
public sealed class GridBarrierTests
{
    private const int Untouched = -7;
    private const int BlockSize = 128;
    private const int Blocks = 4;
    private const int Rounds = 64;

    // CUDA_ERROR_COOPERATIVE_LAUNCH_TOO_LARGE
    private const int CooperativeLaunchTooLarge = 720;

    // The sum each surviving thread of GridSyncAfterFailKernel adds up: the next block writes round + 1 in every round
    private const int AfterFailSum = Rounds * (Rounds + 1) / 2;

#if GSHARP_CUDA
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_PartOfEveryWarpAndThreadZeroFail_OthersPassGridBarriers()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Every third thread fails, thread 0 of every block among them, so the arriving thread of each block is elected
        // among the others; thread 1 writes the block value
        var keys = new int[Blocks * BlockSize];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = i % BlockSize % 3 == 0 ? GridBarrierKernels.FailKey : i % 2;
        }

        AssertGridSyncAfterFail(keys);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_WholeWarpsFail_OthersPassGridBarriers()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Threads 32 to 95 of every block, two whole warps, fail; in block 2 the first warp fails as well, thread 1 aside
        var keys = new int[Blocks * BlockSize];
        for (int b = 0; b < Blocks; b++)
        {
            for (int t = 32; t < 96; t++)
            {
                keys[b * BlockSize + t] = GridBarrierKernels.FailKey;
            }
        }

        keys[2 * BlockSize] = GridBarrierKernels.FailKey;
        for (int t = 2; t < 32; t++)
        {
            keys[2 * BlockSize + t] = GridBarrierKernels.FailKey;
        }

        AssertGridSyncAfterFail(keys);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CooperativeLaunchWithoutBarrier_FailingThreadExits()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        int[] keys = [0, GridBarrierKernels.FailKey, 1, 0];
        int[] result = Filled(keys.Length);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierKernels.LaunchCooperativeFailKernel(keys, result, blockDimX: (uint)keys.Length));

        Assert.Null(ex.InnerException);
        Assert.Equal([0, Untouched, -2, 3], result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LargestCooperativeGrid_ThousandBarrierRounds_AreExact()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int ThreadsPerBlock = 256;
        const int ThousandRounds = 1000;

        // The largest grid that a cooperative launch of this kernel accepts: blocks per multiprocessor from 32 down
        int multiprocessors = MultiprocessorCount();
        for (int perMultiprocessor = 32; perMultiprocessor > 0; perMultiprocessor--)
        {
            int blocks = multiprocessors * perMultiprocessor;
            var buffer = new int[2 * blocks * ThreadsPerBlock];
            try
            {
                GridBarrierKernels.LaunchGridSyncRoundsKernel(buffer, ThousandRounds, gridDimX: (uint)blocks,
                    blockDimX: ThreadsPerBlock);
            }
            catch (CudaException ex) when ((int)ex.Result == CooperativeLaunchTooLarge)
            {
                continue;
            }

            // An even number of rounds ends in the first half; every value there is the number of rounds
            int threads = blocks * ThreadsPerBlock;
            for (int i = 0; i < threads; i++)
            {
                Assert.Equal(ThousandRounds, buffer[i]);
            }

            return;
        }

        Assert.Fail("No cooperative grid of this kernel could be launched.");
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_WritesBeforeBarrier_AreVisibleToOtherBlocksAfterIt()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int ThreadsPerBlock = 64;
        int blocks = MultiprocessorCount();
        int threads = blocks * ThreadsPerBlock;
        var values = new int[threads];
        var result = new int[threads];

        GridBarrierKernels.LaunchCrossBlockVisibilityKernel(values, result, gridDimX: (uint)blocks,
            blockDimX: ThreadsPerBlock);

        for (int i = 0; i < threads; i++)
        {
            Assert.Equal(3 * ((i + ThreadsPerBlock) % threads) + 1, result[i]);
        }
    }

    // [WHY] A block whose threads all failed never arrives at the grid barrier; the waiters see the recorded error and
    // trap instead of waiting forever: a real GPU fault, run in a child process of its own (with the timeout of
    // IsolatedTestProcess, so a hang fails the test) and only in `just test-gpu-trap`
    [SkippableFact]
    [Category(TestCategory.GpuTrap)]
    [Scenario(TestScenario.SadPath)]
    public void Test_WholeBlockFailsBeforeGridBarrier_WaitersTrapInsteadOfHanging()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        if (!IsolatedTestProcess.IsChild(nameof(Test_WholeBlockFailsBeforeGridBarrier_WaitersTrapInsteadOfHanging)))
        {
            CudaTrapRuns.RunIsolated(typeof(GridBarrierTests),
                nameof(Test_WholeBlockFailsBeforeGridBarrier_WaitersTrapInsteadOfHanging));
            return;
        }

        // Every thread of block 1 fails
        var keys = new int[Blocks * BlockSize];
        for (int t = 0; t < BlockSize; t++)
        {
            keys[BlockSize + t] = GridBarrierKernels.FailKey;
        }

        int[] result = Filled(keys.Length);
        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(keys, result, gridDimX: Blocks,
                blockDimX: BlockSize));
        Assert.Equal("(1,0,0)", ex.Data["GSharp.BlockIndex"]);
        var cudaEx = Assert.IsType<CudaException>(ex.InnerException);
        Assert.True(cudaEx.Result is CUresult.CudaErrorIllegalInstruction or CUresult.CudaErrorLaunchFailed);
    }

    // [WHY] A host synchronization may consume and clear the error slot while the waiters of a block that never arrives
    // still wait; a waiter that saw the error keeps it and traps instead of waiting forever. A real GPU fault, run in a
    // child process of its own (with the timeout of IsolatedTestProcess, so a hang fails the test) and only in
    // `just test-gpu-trap`
    [SkippableFact]
    [Category(TestCategory.GpuTrap)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SlotConsumedWhileBlockNeverArrives_WaitersStillTrap()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        if (!IsolatedTestProcess.IsChild(nameof(Test_SlotConsumedWhileBlockNeverArrives_WaitersStillTrap)))
        {
            CudaTrapRuns.RunIsolated(typeof(GridBarrierTests), nameof(Test_SlotConsumedWhileBlockNeverArrives_WaitersStillTrap));
            return;
        }

        var keys = new int[Blocks * BlockSize];
        for (int t = 0; t < BlockSize; t++)
        {
            keys[BlockSize + t] = GridBarrierKernels.FailKey;
        }

        int[] result = Filled(keys.Length);

        // From 20 ms after the start, well after the waiters first looked at the slot (every 1024 polls), another thread
        // consumes the record, as the synchronization of another stream would
        GSharpDeviceErrorSlot consumed = default;
        bool consumedAny = false;
        var consumer = new Thread(() =>
        {
            Thread.Sleep(20);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!consumedAny && DateTime.UtcNow < deadline)
            {
                consumedAny = DeviceErrorSlot.TryConsumeSlot(out consumed);
                Thread.Sleep(1);
            }
        });
        consumer.Start();

        var ex = Assert.ThrowsAny<Exception>(() =>
            GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(keys, result, gridDimX: Blocks,
                blockDimX: BlockSize));
        consumer.Join();

        Assert.True(consumedAny, "the consumer found no record");
        Assert.Equal(1u, consumed.BlockX);
        // The record is gone, so the launch reports the trap of the waiters alone
        var cudaEx = Assert.IsType<CudaException>(ex);
        Assert.True(cudaEx.Result is CUresult.CudaErrorIllegalInstruction or CUresult.CudaErrorLaunchFailed);
    }

    private static void AssertGridSyncAfterFail(int[] keys)
    {
        int[] result = Filled(keys.Length);
        var blockValues = new int[Blocks];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierKernels.LaunchGridSyncAfterFailKernel(keys, Rounds, blockValues, result, gridDimX: Blocks,
                blockDimX: BlockSize));
        Assert.Null(ex.InnerException);

        for (int i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i] == GridBarrierKernels.FailKey ? Untouched : AfterFailSum, result[i]);
        }
    }

    private static int MultiprocessorCount()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(
            CudaDriverApi.cuDeviceGetAttribute(out var count, CudaDriverApi.CuDeviceAttribute.MultiprocessorCount, device)
        );
        return count;
    }
#endif

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
