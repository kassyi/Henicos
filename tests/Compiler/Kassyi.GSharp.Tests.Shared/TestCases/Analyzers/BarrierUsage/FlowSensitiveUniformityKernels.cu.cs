using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// GSH0083 follows the control flow graph (#447, #446): a value is thread-variant at a point only when some path to it
// assigns a thread-variant value, and a barrier is divergent when a thread-variant branch decides whether a thread
// reaches it while the threads that skip it reach another split point. A barrier that the skipping threads pass only
// on their way out of the kernel is reported by a later stage, not here.
public static partial class FlowSensitiveUniformityKernels
{
    [GpuDeviceFunction]
    public static bool WaitAndConfirm()
    {
        Gpu.SyncThreads();
        return true;
    }

    // #447: the local is assigned a uniform value again before the branch
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReassignedUniformKernel(int[] buffer)
    {
        int x = ThreadIndex.X;
        buffer[x] = 1;
        x = 0;
        if (x == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // #447b: the thread-variant value is assigned only after the branch
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void AssignedAfterBranchKernel(int[] buffer, int mode)
    {
        int x = mode;
        if (x == 0)
        {
            Gpu.SyncThreads();
        }

        x = ThreadIndex.X;
        buffer[x] = 1;
        Gpu.SyncThreads();
    }

    // A local assigned under a thread-variant condition is assigned a uniform value after the branches join
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReassignedAfterJoinKernel(int[] buffer)
    {
        int x = 0;
        if (ThreadIndex.X == 0)
        {
            x = 1;
        }

        x = 2;
        if (x == 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // The thread-variant value reaches the condition on the back edge of the loop
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void LoopCarriedKernel(int[] buffer, int count)
    {
        int x = 0;
        for (int i = 0; i < count; i++)
        {
            if (x > 0)
            {
                Gpu.SyncThreads(); // GSH0083
            }

            x = ThreadIndex.X;
        }
    }

    // #446-2: the elements of a deconstruction
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DeconstructionKernel(int[] buffer)
    {
        (int a, int b) = (ThreadIndex.X, 0);
        if (b == 0)
        {
            Gpu.SyncThreads();
        }

        if (a < 16)
        {
            Gpu.SyncThreads(); // GSH0083
        }

        Gpu.SyncThreads();
    }

    // The number of iterations differs between threads, and every thread meets again after the loop
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void VariantWhileKernel(int[] buffer, int count)
    {
        int i = ThreadIndex.X;
        while (i < count)
        {
            Gpu.SyncThreads(); // GSH0083
            i += BlockDimension.X;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SwitchSectionKernel(int[] buffer)
    {
        switch (ThreadIndex.X % 4)
        {
            case 0:
                Gpu.SyncThreads(); // GSH0083
                break;
            default:
                buffer[0] = 0;
                break;
        }

        Gpu.SyncThreads();
    }

    // The right operand of && and a branch of ?: run only for some threads
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConditionalOperandKernel(int[] buffer)
    {
        bool done = ThreadIndex.X < 16 && WaitAndConfirm(); // GSH0083
        bool other = ThreadIndex.X > 16 ? WaitAndConfirm() : false; // GSH0083
        buffer[ThreadIndex.X] = done || other ? 1 : 0;
        Gpu.SyncThreads();
    }

    // A thread that continues meets the barrier again in the next iteration
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void VariantContinueKernel(int[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (ThreadIndex.X == i)
            {
                continue;
            }

            Gpu.SyncThreads(); // GSH0083
        }
    }

    // A thread that breaks out of the loop meets the barrier after it
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void VariantBreakKernel(int[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (ThreadIndex.X == i)
            {
                break;
            }

            Gpu.SyncThreads(); // GSH0083
        }

        Gpu.SyncThreads();
    }

    // B2: a grid barrier must be reached by every thread of the grid, so a branch on the block index diverges for it but
    // not for a block barrier
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GridBarrierKernel(int[] buffer)
    {
        if (BlockIndex.X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        if (BlockIndex.X == 0)
        {
            Gpu.SyncGrid(); // GSH0083
        }

        if (ThreadIndex.X == 0)
        {
            Gpu.SyncGrid(); // GSH0083
        }

        Gpu.SyncGrid();
    }

    // Exit only: the threads that skip the barrier leave the kernel without reaching another split point (a later
    // stage reports these as a warning)
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void EarlyReturnKernel(int[] buffer)
    {
        if (ThreadIndex.X > 8)
        {
            return;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BarrierAtEndKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            Gpu.SyncThreads();
        }
    }

    // A local assigned under a thread-variant condition differs where the branches meet again
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConditionallyAssignedKernel(int[] buffer)
    {
        int flag = 0;
        if (ThreadIndex.X == 0)
        {
            flag = 1;
        }

        if (flag == 1)
        {
            Gpu.SyncThreads(); // GSH0083
        }

        Gpu.SyncThreads();
    }

    // A body that does not bind while it is being edited is not analyzed
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void EditedBodyKernel(int[] buffer)
    {
        int x = NotDeclaredYet;
        if (ThreadIndex.X < x)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }
}
