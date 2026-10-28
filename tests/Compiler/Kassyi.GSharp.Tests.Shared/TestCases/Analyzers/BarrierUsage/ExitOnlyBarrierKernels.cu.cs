using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Core.Gpu;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// Exit only: a thread-variant branch decides whether a thread reaches the barrier, and the threads that skip it leave
// the kernel (or the device function) without reaching another split point. These are the shapes the GSH0037 tests
// used before the barrier analysis followed the control flow graph. They are not GSH0037; a later stage reports them
// as a warning.
public static partial class ExitOnlyBarrierKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void IfBranchBarrierKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ElseBranchBarrierKernel(int[] buffer)
    {
        if (buffer[ThreadIndex.X] > 0)
        {
            buffer[0] = 1;
        }
        else
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SwitchBranchBarrierKernel(int[] buffer)
    {
        switch (ThreadIndex.X % 2)
        {
            case 0:
                Gpu.SyncThreads();
                break;
            default:
                buffer[0] = 0;
                break;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConditionallyAssignedLocalKernel(int[] buffer)
    {
        int flag = 0;
        if (ThreadIndex.X == 0)
        {
            flag = 1;
        }

        if (flag == 1)
        {
            Gpu.SyncThreads();
        }
    }

    // The number of iterations differs between threads, and nothing follows the loop
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ThreadStrideLoopKernel(int[] buffer, int count)
    {
        for (int i = ThreadIndex.X; i < count; i += BlockDimension.X)
        {
            buffer[i] += 1;
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    public static void WaitIfFirstHalf()
    {
        if (ThreadIndex.X < 16)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CallerKernel(int[] buffer)
    {
        WaitIfFirstHalf();
    }

    [GpuDeviceFunction]
    public static void WaitForBlock()
    {
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DivergentCallKernel(int[] buffer)
    {
        if (ThreadIndex.X == 0)
        {
            WaitForBlock();
        }
    }

    [GpuDeviceFunction]
    public static bool WaitAndConfirm()
    {
        Gpu.SyncThreads();
        return true;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ShortCircuitCallKernel(int[] buffer)
    {
        bool done = ThreadIndex.X < 16 && WaitAndConfirm();
        buffer[ThreadIndex.X] = done ? 1 : 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UsingStaticInBranchKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            SyncThreads();
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GridBarrierInBranchKernel(int[] buffer)
    {
        if (BlockIndex.X == 0)
        {
            Gpu.SyncGrid();
        }
    }
}
