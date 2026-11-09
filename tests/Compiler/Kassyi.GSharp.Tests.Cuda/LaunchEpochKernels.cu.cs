using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Kernels for <see cref="LaunchEpochTests"/>. They spend a given number of clock cycles of the multiprocessor
/// (<c>Gpu.Clock64</c>, CUDA only) after a thread may fail, so that a test can order a failure against the end of
/// another launch or a host synchronization. A thread fails when its key is <see cref="FailKey"/>.
/// </summary>
public static partial class LaunchEpochKernels
{
    /// <summary>The key that makes a thread fail.</summary>
    public const int FailKey = 2;

#pragma warning disable CS8509 // The switch expressions below leave FailKey unmatched on purpose
    // Every thread that does not fail spends <cycles> clock cycles, then writes its value
    [GpuKernel]
    public static void DelayedWriteKernel(int[] keys, long cycles, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        ulong start = Gpu.Clock64();
        while ((long)(Gpu.Clock64() - start) < cycles)
        {
        }

        result[tid] = value;
    }

    // Every thread that does not fail spends <cycles> clock cycles before the grid barrier, then writes its value after
    // it; a block whose threads all fail never arrives
    [GpuKernel(Cooperative = true)]
    public static void DelayedGridSyncAfterFailKernel(int[] keys, long cycles, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        ulong start = Gpu.Clock64();
        while ((long)(Gpu.Clock64() - start) < cycles)
        {
        }

        Gpu.SyncGrid();
        result[tid] = value;
    }

    // Every thread but <failingThread> writes its index
    [GpuKernel]
    public static void FillUnlessFailKernel(int failingThread, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int key = tid == failingThread ? FailKey : 0;
        result[tid] = key switch { 0 => tid };
    }
#pragma warning restore CS8509
}
