using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// The CUDA grid barrier (<c>Gpu.SyncGrid</c>): once a thread of the launch failed, every block leaves the barrier
/// instead of running the code after it, whether threads, warps, thread (0,0,0) or a whole block failed; repeated
/// barriers over the largest cooperative grid stay exact; and the writes before a barrier are visible to every block
/// after it.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public sealed class GridBarrierTests
{
    private const int Untouched = -7;
    private const int BlockSize = 128;
    private const int Blocks = 4;
    private const int Rounds = 64;

    // CUDA_ERROR_COOPERATIVE_LAUNCH_TOO_LARGE
    private const int CooperativeLaunchTooLarge = 720;

#if GSHARP_CUDA
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GridBarrier_PartOfEveryWarpAndThreadZeroFail_EveryBlockLeavesBarrier()
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
    public void GridBarrier_WholeWarpsFail_EveryBlockLeavesBarrier()
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

    // [WHY] Block 0 still has threads that reach the barrier after its thread 5 failed. Its arriving thread sees the
    // abort word and does not arrive, so the barrier does not complete and the healthy blocks that wait at it leave as
    // well; an arrival would complete the barrier and let them run the code after it. The timeout fails the test
    // instead of waiting for a hang that the driver would end only by resetting the GPU.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task GridBarrier_OneThreadFailsInOneBlock_NoBlockPassesBarrier()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierKernels.FailKey;
        int[] result = Filled(keys.Length);

        var launch = Task.Run(() => Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(keys, result, gridDimX: Blocks,
                blockDimX: BlockSize)));
        var ex = await launch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CooperativeLaunch_WithoutBarrier_FailingThreadExits()
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
    public void GridBarrier_LargestCooperativeGrid_ExecutesThousandRoundsExactly()
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
    public void GridBarrier_WritesBeforeBarrier_VisibleToOtherBlocksAfterBarrier()
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

    // [WHY] A block whose threads all failed never arrives at the grid barrier; the waiters read the abort word of
    // their launch, which the failing threads set, and the threads of their blocks exit after the barrier instead of
    // running the code after it. The timeout fails the test instead of waiting for a hang that the driver would end
    // only by resetting the GPU. A host synchronization that consumes the record first does not keep the waiters
    // waiting (LaunchEpochTests in Tests.Cuda, which delays the barrier).
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task GridBarrier_WholeBlockFailsBeforeGridBarrier_WaitersLeaveAndLaunchThrows()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Every thread of block 1 fails
        var keys = new int[Blocks * BlockSize];
        for (int t = 0; t < BlockSize; t++)
        {
            keys[BlockSize + t] = GridBarrierKernels.FailKey;
        }

        int[] result = Filled(keys.Length);
        var launch = Task.Run(() => Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierKernels.LaunchWholeBlockFailsBeforeGridSyncKernel(keys, result, gridDimX: Blocks,
                blockDimX: BlockSize)));
        var ex = await launch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("(1,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
        // The barrier never completes, so no thread writes after it
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    private static void AssertGridSyncAfterFail(int[] keys)
    {
        int[] result = Filled(keys.Length);
        var blockValues = new int[Blocks];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierKernels.LaunchGridSyncAfterFailKernel(keys, Rounds, blockValues, result, gridDimX: Blocks,
                blockDimX: BlockSize));
        Assert.Null(ex.InnerException);

        // The threads failed before the first barrier, so every block leaves it: no surviving thread gets to its write.
        // A launch captured into a graph does the same (LaunchEpochTests in Tests.Cuda).
        Assert.All(result, static value => Assert.Equal(Untouched, value));
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
