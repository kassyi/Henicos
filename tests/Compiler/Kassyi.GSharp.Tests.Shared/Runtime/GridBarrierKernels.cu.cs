using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Cooperative kernels for <see cref="GridBarrierTests"/>: the grid barrier with threads, warps and blocks that failed
/// and exited before it, many rounds of it, and the order of memory around it. A thread fails when its key is
/// <see cref="FailKey"/>: no arm of the switch expression matches it.
/// </summary>
[SkipSnapshotConsistency]
public static partial class GridBarrierKernels
{
    /// <summary>The key that makes a thread fail.</summary>
    public const int FailKey = 2;

#pragma warning disable CS8509 // The switch expressions below leave FailKey unmatched on purpose
    // The threads that do not fail pass the grid barrier 2 * <rounds> times; in each round thread 1 of every block writes
    // a value and every surviving thread adds up the value of the next block, so a barrier that waited for a failed thread
    // would never complete and one that did not order the write would add a stale value
    [GpuKernel(Cooperative = true)]
    public static void GridSyncAfterFailKernel(int[] keys, int rounds, int[] blockValues, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int step = keys[tid] switch { 0 => 1, 1 => 1 };
        int sum = 0;
        for (int round = 0; round < rounds; round++)
        {
            if (ThreadIndex.X == 1)
            {
                blockValues[BlockIndex.X] = round + step;
            }

            Gpu.SyncGrid();
            sum += blockValues[(BlockIndex.X + 1) % GridDimension.X];
            Gpu.SyncGrid();
        }

        result[tid] = sum;
    }

    // Every thread of the blocks whose keys are FailKey fails, so those blocks never reach the barrier
    [GpuKernel(Cooperative = true)]
    public static void WholeBlockFailsBeforeGridSyncKernel(int[] keys, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        Gpu.SyncGrid();
        result[tid] = value;
    }

    // A failing thread of a cooperative launch with no barrier after it exits like in any other launch
    [GpuKernel(Cooperative = true)]
    public static void CooperativeFailKernel(int[] keys, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[tid] = keys[tid] switch { 0 => tid, 1 => -tid };
    }
#pragma warning restore CS8509

    // <rounds> rounds over two halves of buffer (each as long as the grid): in round r every thread reads the value that
    // the thread one block further on wrote into one half in round r - 1, adds 1 and writes it into the other half. Each
    // value is r after round r only if every barrier waited for every block and made its writes visible.
    [GpuKernel(Cooperative = true)]
    public static void GridSyncRoundsKernel(int[] buffer, int rounds)
    {
        int threads = GridDimension.X * BlockDimension.X;
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int neighbor = (tid + BlockDimension.X) % threads;
        for (int round = 0; round < rounds; round++)
        {
            int from = (round & 1) * threads;
            int to = threads - from;
            buffer[to + tid] = buffer[from + neighbor] + 1;
            Gpu.SyncGrid();
        }
    }

    // Every thread writes its value before the barrier and reads the value of the same thread of the next block after it
    [GpuKernel(Cooperative = true)]
    public static void CrossBlockVisibilityKernel(int[] values, int[] result)
    {
        int threads = GridDimension.X * BlockDimension.X;
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        values[tid] = 3 * tid + 1;
        Gpu.SyncGrid();
        result[tid] = values[(tid + BlockDimension.X) % threads];
    }
}
