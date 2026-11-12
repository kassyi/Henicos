using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Kernels for <see cref="CapturedBarrierTests"/>: grid barriers in both modes with a failure at a chosen round, and an
/// mbarrier with a late arrival. A thread fails when its key is <see cref="FailKey"/>.
/// </summary>
[SkipSnapshotConsistency]
public static partial class CapturedBarrierKernels
{
    /// <summary>The key that makes a thread fail.</summary>
    public const int FailKey = 2;

#pragma warning disable CS8509 // The switch expressions below leave FailKey unmatched on purpose
    // The threads whose key is FailKey fail at the start of round control[0]; the others pass 2 * <rounds> grid
    // barriers and add up the value that thread 1 of the next block wrote before each first barrier, round + 1
    [GpuKernel(Cooperative = true)]
    public static void FailAtRoundKernel(int[] keys, int rounds, int[] control, int[] blockValues, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int sum = 0;
        for (int round = 0; round < rounds; round++)
        {
            if (round == control[0])
            {
                sum += keys[tid] switch { 0 => 0, 1 => 0 };
            }

            if (ThreadIndex.X == 1)
            {
                blockValues[BlockIndex.X] = round + 1;
            }

            Gpu.SyncGrid();
            sum += blockValues[(BlockIndex.X + 1) % GridDimension.X];
            Gpu.SyncGrid();
        }

        result[tid] = sum;
    }

    // The same with the Fast grid barrier
    [GpuKernel(Cooperative = true)]
    public static void FastFailAtRoundKernel(int[] keys, int rounds, int[] control, int[] blockValues, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int sum = 0;
        for (int round = 0; round < rounds; round++)
        {
            if (round == control[0])
            {
                sum += keys[tid] switch { 0 => 0, 1 => 0 };
            }

            if (ThreadIndex.X == 1)
            {
                blockValues[BlockIndex.X] = round + 1;
            }

            Gpu.SyncGrid(GridSyncMode.Fast);
            sum += blockValues[(BlockIndex.X + 1) % GridDimension.X];
            Gpu.SyncGrid(GridSyncMode.Fast);
        }

        result[tid] = sum;
    }

    // An mbarrier that expects every thread of the block; thread 33 (the second warp) arrives <cycles> clock cycles
    // late, so the waiters of the first warp poll long enough to read the abort word of their launch
    [GpuKernel]
    public static void DelayedMBarrierKernel(int[] keys, long cycles, int[] result)
    {
        int tid = ThreadIndex.X;
        var bar = new SharedMemory<ulong>(1);
        if (tid == 0)
        {
            Gpu.MBarrierInit(ref bar[0], (uint)BlockDimension.X);
        }

        Gpu.SyncThreads();
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        if (tid == 33)
        {
            ulong start = Gpu.Clock64();
            while ((long)(Gpu.Clock64() - start) < cycles)
            {
            }
        }

        Gpu.MBarrierArrive(ref bar[0]);
        Gpu.MBarrierWait(ref bar[0], 0);
        result[tid] = value;
    }
#pragma warning restore CS8509
}
