using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Core.Gpu;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Cooperative kernels for <see cref="GridSyncModeTests"/>: the grid barrier in each <see cref="GridSyncMode"/>,
/// spelled in the ways a constant mode can be written, many rounds of the Fast barrier, and threads that fail before
/// it. A thread fails when its key is <see cref="FailKey"/>: no arm of the switch expression matches it.
/// </summary>
public static partial class GridSyncModeKernels
{
    /// <summary>The key that makes a thread fail.</summary>
    public const int FailKey = 2;

    private const GridSyncMode ConstantFast = GridSyncMode.Fast;

    // Each thread writes before every barrier and reads the value of the same thread of the next block after it: the
    // default mode, Deterministic written out, and Fast written as the member, a const field and a cast
    [GpuKernel(Cooperative = true)]
    public static void EveryModeSpellingKernel(int[] values, int[] result)
    {
        int threads = GridDimension.X * BlockDimension.X;
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int next = (tid + BlockDimension.X) % threads;
        values[tid] = tid + 1;
        Gpu.SyncGrid();
        int sum = values[next];
        Gpu.SyncGrid(GridSyncMode.Deterministic);
        values[tid] = 2 * (tid + 1);
        Gpu.SyncGrid(GridSyncMode.Fast);
        sum += values[next];
        Gpu.SyncGrid(ConstantFast);
        values[tid] = 3 * (tid + 1);
        Gpu.SyncGrid(mode: (GridSyncMode)1);
        result[tid] = sum + values[next];
    }

    // An explicit Fast barrier, called through using static, as a statement of a Gpu.SyncGrid(body): the body inserts
    // no barrier next to it, and the ones it inserts are Deterministic
    [GpuKernel(Cooperative = true)]
    public static void FastInsideBodyKernel(int[] values, int[] result)
    {
        int threads = GridDimension.X * BlockDimension.X;
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int next = (tid + BlockDimension.X) % threads;
        int sum = 0;
        Gpu.SyncGrid(() =>
        {
            values[tid] = tid + 1;
            sum = values[next];
            SyncGrid(GridSyncMode.Fast);
            values[tid] = 2 * (tid + 1);
            sum += values[next];
        });
        result[tid] = sum;
    }

    [GpuKernel(Cooperative = true)]
    public static void DeterministicOnlyKernel(int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(GridSyncMode.Deterministic);
        result[tid] = tid;
    }

    [GpuKernel(Cooperative = true)]
    public static void FastOnlyKernel(int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(GridSyncMode.Fast);
        result[tid] = tid;
    }

    // <rounds> rounds over two halves of buffer (each as long as the grid): in round r every thread reads the value
    // that the thread one block further on wrote into one half in round r - 1, adds 1 and writes it into the other
    // half. Each value is r after round r only if every Fast barrier waited for every block and made its writes
    // visible.
    [GpuKernel(Cooperative = true)]
    public static void FastRoundsKernel(int[] buffer, int rounds)
    {
        int threads = GridDimension.X * BlockDimension.X;
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int neighbor = (tid + BlockDimension.X) % threads;
        for (int round = 0; round < rounds; round++)
        {
            int from = (round & 1) * threads;
            int to = threads - from;
            buffer[to + tid] = buffer[from + neighbor] + 1;
            Gpu.SyncGrid(GridSyncMode.Fast);
        }
    }

#pragma warning disable CS8509 // The switch expression below leaves FailKey unmatched on purpose
    // The threads whose keys are FailKey fail before the Fast barrier
    [GpuKernel(Cooperative = true)]
    public static void FailBeforeFastGridSyncKernel(int[] keys, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        Gpu.SyncGrid(GridSyncMode.Fast);
        result[tid] = value;
    }

    // The threads whose keys are FailKey fail first; in every round thread 1 of each block writes round + 1 before a
    // Fast barrier and every surviving thread adds the value of the next block after it
    [GpuKernel(Cooperative = true)]
    public static void FastSyncAfterFailKernel(int[] keys, int rounds, int[] blockValues, int[] result)
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

            Gpu.SyncGrid(GridSyncMode.Fast);
            sum += blockValues[(BlockIndex.X + 1) % GridDimension.X];
            Gpu.SyncGrid(GridSyncMode.Fast);
        }

        result[tid] = sum;
    }
#pragma warning restore CS8509
}
