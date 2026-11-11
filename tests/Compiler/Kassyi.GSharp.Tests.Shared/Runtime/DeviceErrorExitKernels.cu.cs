using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Kernels in which some threads fail a device check while the others go on, for <see cref="DeviceErrorExitTests"/>.
/// A thread fails when its key is <see cref="FailKey"/>: no arm of the switch expression matches it.
/// </summary>
[SkipSnapshotConsistency]
public static partial class DeviceErrorExitKernels
{
    /// <summary>The key that makes a thread fail.</summary>
    public const int FailKey = 2;

#pragma warning disable CS8509 // The switch expressions below leave FailKey unmatched on purpose
    // Every thread writes its value; a failing thread writes nothing
    [GpuKernel]
    public static void WriteUnlessFailKernel(int[] keys, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[tid] = keys[tid] switch { 0 => tid, 1 => -tid };
    }

    // The threads that do not fail pass the block barrier <rounds> times and add up a shared value written before each
    // barrier, so a barrier that waited for a failed thread would never complete
    [GpuKernel]
    public static void BarrierLoopAfterFailKernel(int[] keys, int rounds, int[] result)
    {
        var shared = new SharedMemory<int>(1);
        int tid = ThreadIndex.X;
        int step = keys[tid] switch { 0 => 1, 1 => 1 };
        int sum = 0;
        for (int round = 0; round < rounds; round++)
        {
            if (ThreadIndex.X == 1)
            {
                shared[0] = round + step;
            }

            Gpu.SyncThreads();
            sum += shared[0];
            Gpu.SyncThreads();
        }

        result[tid] = sum;
    }

    // An mbarrier that expects every thread of the block: a failing thread never arrives, so the waiters see the abort
    // word of their launch and exit
    [GpuKernel]
    public static void MBarrierFailKernel(int[] keys, int[] result)
    {
        int tid = ThreadIndex.X;
        var bar = new SharedMemory<ulong>(1);
        if (tid == 0)
        {
            Gpu.MBarrierInit(ref bar[0], (uint)BlockDimension.X);
        }

        Gpu.SyncThreads();
        int value = keys[tid] switch { 0 => tid, 1 => -tid };
        Gpu.MBarrierArrive(ref bar[0]);
        Gpu.MBarrierWait(ref bar[0], 0);
        result[tid] = value;
    }
#pragma warning restore CS8509
}
