using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// Exit-only guards followed by uniform control flow: the guard decides only which threads leave, not which of the
// remaining threads reach a barrier behind a uniform branch (#772 second review)
public static partial class GuardThenUniformKernels
{
    // A guard, then a uniform if around a barrier, then a barrier: exit only (GSH0084 at the guard)
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GuardThenUniformIfKernel(int[] buffer, int count)
    {
        if (ThreadIndex.X >= count)
        {
            return;
        }

        if (count > 1)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A guard, then a uniform loop with a barrier inside: exit only
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GuardThenUniformLoopKernel(int[] buffer, int count)
    {
        if (ThreadIndex.X >= count)
        {
            return;
        }

        buffer[ThreadIndex.X] = 0;
        Gpu.SyncThreads();
        for (int i = 0; i < count; i++)
        {
            buffer[ThreadIndex.X] += i;
            Gpu.SyncThreads();
        }
    }

    // The same guard without the barrier before the loop
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GuardThenLoopOnlyKernel(int[] buffer, int count)
    {
        if (ThreadIndex.X >= count)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            buffer[ThreadIndex.X] += i;
            Gpu.SyncThreads();
        }
    }

    // A uniform if with a barrier on both arms, after a guard
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GuardThenUniformIfElseKernel(int[] buffer, int count)
    {
        if (ThreadIndex.X >= count)
        {
            return;
        }

        if (count > 1)
        {
            Gpu.SyncThreads();
        }
        else
        {
            Gpu.SyncThreads();
        }
    }

    // A divergent if whose body has a barrier, followed by a uniform if with another barrier: the threads that skip the
    // first barrier wait at the second (GSH0083)
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DivergentThenUniformKernel(int[] buffer, int count)
    {
        if (ThreadIndex.X < 16)
        {
            Gpu.SyncThreads();
        }

        if (count > 1)
        {
            Gpu.SyncThreads();
        }
    }

    // A guard inside a uniform loop that continues: the threads that continue wait at the barrier of the next iteration
    // while the others wait at this one (GSH0083)
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GuardContinueInLoopKernel(int[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (ThreadIndex.X >= i)
            {
                continue;
            }

            if (count > 1)
            {
                Gpu.SyncThreads();
            }
        }
    }
}
