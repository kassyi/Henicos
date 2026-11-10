using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// A cooperative kernel for <see cref="GridBarrierTimingTests"/>. It reads the clock of the multiprocessor
/// (<c>Gpu.Clock64</c>), which only the CUDA dialect provides. A thread fails when its key is <see cref="FailKey"/>.
/// </summary>
public static partial class GridBarrierTimingKernels
{
    /// <summary>The key that makes a thread fail.</summary>
    public const int FailKey = 2;

#pragma warning disable CS8509 // The switch expression below leaves FailKey unmatched on purpose
    // Block <slowBlock> spends <cycles> clock cycles before the barrier after a thread failed: the waiters see the recorded
    // error, but a block that is only slow arrives within the time they keep waiting
    [GpuKernel(Cooperative = true)]
    public static void SlowBlockAfterFailKernel(int[] keys, int slowBlock, long cycles, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        if (BlockIndex.X == slowBlock)
        {
            ulong start = Gpu.Clock64();
            while ((long)(Gpu.Clock64() - start) < cycles)
            {
            }
        }

        Gpu.SyncGrid();
        result[tid] = value;
    }

    // Block 0 spends <cycles> clock cycles, then its threads whose keys are FailKey fail and the others reach a Fast
    // barrier, at which every other block is already waiting
    [GpuKernel(Cooperative = true)]
    public static void LateFailingBlockFastKernel(int[] keys, long cycles, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        SpinInFirstBlock(cycles);
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        Gpu.SyncGrid(GridSyncMode.Fast);
        result[tid] = value;
    }

    // The same with a Deterministic barrier
    [GpuKernel(Cooperative = true)]
    public static void LateFailingBlockDeterministicKernel(int[] keys, long cycles, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        SpinInFirstBlock(cycles);
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        Gpu.SyncGrid(GridSyncMode.Deterministic);
        result[tid] = value;
    }
#pragma warning restore CS8509

    [GpuDeviceFunction]
    private static void SpinInFirstBlock(long cycles)
    {
        if (BlockIndex.X == 0)
        {
            ulong start = Gpu.Clock64();
            while ((long)(Gpu.Clock64() - start) < cycles)
            {
            }
        }
    }
}
