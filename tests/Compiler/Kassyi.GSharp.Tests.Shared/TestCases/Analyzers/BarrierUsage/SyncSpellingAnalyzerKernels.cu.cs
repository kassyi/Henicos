using System.Collections.Generic;
using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Core.Gpu;
using G = Kassyi.GSharp.Core.Gpu;
using W = Kassyi.GSharp.Core.Warp;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// The barrier is recognized by its symbol however it is spelled (#503)
public static partial class SyncSpellingAnalyzerKernels
{
    // GSH0042: a barrier through using static in a method that is not a kernel or device function
    public static void HostHelper()
    {
        SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void UsingStaticInBranchKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            SyncThreads(); // GSH0037
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void AliasInBranchKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            G.SyncThreads(); // GSH0037
        }
    }

    [GpuDeviceFunction]
    public static void SyncQualified() => Kassyi.GSharp.Core.Gpu.SyncThreads();

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void QualifiedDeviceFunctionInBranchKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            SyncQualified(); // GSH0037: the callee reaches a block barrier
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> ShuffledValues(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return W.ShuffleDownSync(0xFFFFFFFFu, i, 1u); // GSH0072: a shuffle splits the state machine
        }
    }

    [GpuDeviceFunction]
    public static void GridSync() => Gpu.SyncGrid();

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Sad Path")]
    public static void GridSyncDeviceFunctionInBranchKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            GridSync(); // GSH0037: the callee reaches a grid barrier, which every thread must reach as well
        }
    }
}
