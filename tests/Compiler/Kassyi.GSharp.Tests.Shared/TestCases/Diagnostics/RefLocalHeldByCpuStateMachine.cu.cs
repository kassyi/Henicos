using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// Ref locals that the CPU state machine would keep in its frame although GSH0039 does not report them: each one is
// GSH0003 from the generator on the CPU dialect (#1640). A ref foreach variable is held even when it is not live across
// the barrier: its span source cannot cross a split on the managed path yet (#1005)
[SkipSnapshotConsistency("Diagnostics case")]
public static partial class RefLocalHeldByCpuStateMachineKernels
{
    // Rebound before the barrier: not live across it
    [GpuKernel]
    public static void ReboundBeforeSyncThreads(int[] r)
    {
        int t = ThreadIndex.X;
        int x = r[t];
        int y = r[t] + 100;
        ref int alias = ref x;
        alias = ref y;
        alias += 1;
        Gpu.SyncThreads();
        r[t] = y;
    }

    // Used in a switch section after the one with the barrier
    [GpuKernel]
    public static void UsedInAnotherSwitchSection(int[] r)
    {
        int t = ThreadIndex.X;
        int w = r[t];
        ref int alias = ref w;
        switch (BlockIndex.X)
        {
            case 0:
                alias += 1;
                Gpu.SyncThreads();
                break;
            default:
                alias += 2;
                break;
        }

        r[t] = w;
    }

    // A ref foreach variable read after the barrier of its iteration
    [GpuKernel]
    public static void RefForEachReadAfterSyncThreads(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncThreads();
            e += 1;
        }
    }

    // The same in a device function that may suspend
    [GpuDeviceFunction]
    public static void RefForEachInDeviceFunction(Span<int> s)
    {
        foreach (ref int e in s)
        {
            Gpu.SyncThreads();
            e += 1;
        }
    }

    // A fresh ref foreach variable in every iteration, used only before the barrier
    [GpuKernel]
    public static void RefForEachUsedBeforeSyncThreads(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            e += 1;
            Gpu.SyncThreads();
        }
    }
}
