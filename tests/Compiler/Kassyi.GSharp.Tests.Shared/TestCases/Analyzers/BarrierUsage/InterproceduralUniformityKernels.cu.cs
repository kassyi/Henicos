using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// C6: a device function whose barrier depends on its argument is reported where it is called (#446-1); the bodies of
// the macros that the CPU dialects do not split cannot hold a barrier; GSH0037 (error) only for a branch that certainly
// diverges (thread 0 and thread 1 of a block, or block 0 and block 1 of a grid, take different sides), GSH0083
// (warning) for one that depends on the launch configuration or on run-time values.
public static partial class InterproceduralUniformityKernels
{
    // Diverges only when its argument does
    [GpuDeviceFunction]
    public static void SyncIfEven(int value)
    {
        if (value % 2 == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void SyncThrough(int value)
    {
        SyncIfEven(value);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ArgumentKernel(int[] buffer, int mode)
    {
        SyncIfEven(mode);
        SyncIfEven(ThreadIndex.X); // GSH0037: the thread index itself, and the callee certainly diverges on it
        SyncIfEven(ThreadIndex.X + 1); // GSH0083: not the thread index itself
        SyncThrough(ThreadIndex.X); // GSH0037: two calls deep
        Gpu.SyncThreads();
    }

    // Leaves early only in some threads; the barrier after it is exit only inside the function
    [GpuDeviceFunction]
    public static void ExitIfOdd()
    {
        if (ThreadIndex.X % 2 == 1)
        {
            return;
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void ExitIfBeyond(int count)
    {
        if (ThreadIndex.X >= count)
        {
            return;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ExitInCalleeKernel(int[] buffer, int count)
    {
        ExitIfOdd(); // GSH0037: the threads that left meet the others at the barrier below
        ExitIfBeyond(count); // GSH0083
        Gpu.SyncThreads();
        ExitIfOdd(); // exit only: nothing follows
    }

    // The CPU dialects do not split these bodies into the state machine
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UnsplitBodyKernel(int[] buffer, int count)
    {
        Gpu.GridStrideLoop(count, i =>
        {
            buffer[i] = i;
            Gpu.SyncThreads(); // GSH0037
        });
        Gpu.SingleThread(() =>
        {
            Gpu.SyncThreads(); // GSH0037
        });

        void Local()
        {
            Gpu.SyncThreads(); // GSH0037
        }

        Local();
    }

    // The body of ForUnroll is expanded where it is called, and its index is the same in every thread
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UnrolledBodyKernel(int[] buffer)
    {
        Gpu.ForUnroll(0, 4, i =>
        {
            if (i == 1)
            {
                Gpu.SyncThreads();
            }

            Gpu.SyncThreads();
            if (ThreadIndex.X % 2 == 0)
            {
                Gpu.SyncThreads(); // GSH0037
            }
        });
        Gpu.SyncThreads();
    }

    // Certain forms are errors; forms that depend on the block width or on run-time values are warnings
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CertaintyKernel(int[] buffer, int count)
    {
        int tid = ThreadIndex.X;
        if (tid % 2 == 0)
        {
            Gpu.SyncThreads(); // GSH0037
        }

        if (tid < 16)
        {
            Gpu.SyncThreads(); // GSH0083: every thread takes it when the block has at most 16 threads
        }

        if (ThreadIndex.Y == 0)
        {
            Gpu.SyncThreads(); // GSH0083: every thread takes it in a one-dimensional launch
        }

        if (Gpu.LaneId() == 0)
        {
            Gpu.SyncThreads(); // GSH0037
        }

        if (buffer[tid] > count)
        {
            Gpu.SyncThreads(); // GSH0083: a run-time value
        }

        Gpu.SyncThreads();
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GridCertaintyKernel(int[] buffer)
    {
        if (BlockIndex.X == 0)
        {
            Gpu.SyncGrid(); // GSH0037
        }

        if (BlockIndex.X < 4)
        {
            Gpu.SyncGrid(); // GSH0083
        }

        if (BlockIndex.Y == 0)
        {
            Gpu.SyncGrid(); // GSH0083
        }

        if (ThreadIndex.X == 0)
        {
            Gpu.SyncGrid(); // GSH0037: diverges within every block
        }

        Gpu.SyncGrid();
    }
}
