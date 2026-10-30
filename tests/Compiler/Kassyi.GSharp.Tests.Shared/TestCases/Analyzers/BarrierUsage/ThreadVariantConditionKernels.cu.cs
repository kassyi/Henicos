using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class ThreadVariantConditionKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void DerivedLocalConditionKernel(int[] buffer)
    {
        int lane = ThreadIndex.X % 32;
        bool first = lane == 0;
        if (first)
        {
            // GSH0083: 'first' is derived from ThreadIndex
            Gpu.SyncThreads();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ConditionallyAssignedLocalKernel(int[] buffer)
    {
        int flag = 0;
        if (ThreadIndex.X == 0)
        {
            flag = 1;
        }

        if (flag == 1)
        {
            // GSH0083: 'flag' is assigned only in the threads where ThreadIndex.X == 0
            Gpu.SyncThreads();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ThreadStrideLoopKernel(int[] buffer, int count)
    {
        for (int i = ThreadIndex.X; i < count; i += BlockDimension.X)
        {
            buffer[i] += 1;
            // GSH0083: the number of iterations differs between threads
            Gpu.SyncThreads();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LaneResultConditionKernel(int[] buffer)
    {
        if (Gpu.LaneId() < 16)
        {
            // GSH0083: the lane id differs between threads
            Gpu.SyncThreads();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }
}
