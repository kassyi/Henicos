using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class UniformConditionBarrierKernels
{
    // Takes each thread's value and returns the first thread's, which every thread reads from shared memory
    [GpuDeviceFunction]
    public static int BroadcastFirst(int value, SharedMemory<int> slot)
    {
        if (ThreadIndex.X == 0)
        {
            slot[0] = value;
        }

        Gpu.SyncThreads();
        int first = slot[0];
        Gpu.SyncThreads();
        return first;
    }

    [GpuDeviceFunction]
    public static void SyncIfRequested(bool syncThreads)
    {
        if (syncThreads)
        {
            // A parameter is assumed to be the same in every thread
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer-only case")]
    public static void UniformConditionKernel(int[] buffer, int mode)
    {
        var slot = new SharedMemory<int>(1);
        if (mode > 0)
        {
            // A kernel argument is the same in every thread
            Gpu.SyncThreads();
        }

        int first = BroadcastFirst(buffer[ThreadIndex.X], slot);
        if (first > 0)
        {
            // The broadcast value is the same in every thread
            SyncIfRequested(true);
        }

        if (buffer[0] > 0)
        {
            // An element read with a uniform index is the same in every thread
            Gpu.SyncThreads();
        }

        for (int i = 0; i < mode; i++)
        {
            Gpu.SyncThreads();
        }
    }
}
