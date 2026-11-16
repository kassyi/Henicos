using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// A foreach (ref ...) whose loop holds a split point is GSH0039 at its variable: a block barrier, a warp shuffle,
// Gpu.SyncWarp, Gpu.SyncGrid or a call that may suspend, which is the rule of the cross-barrier checks. A split point
// outside the loop, or in a lambda or local function of it, does not make the loop one.
public static partial class RefForEachAroundSplitKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    [GpuDeviceFunction]
    public static void Suspends()
    {
        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void SuspendsThroughASecondFunction()
    {
        Suspends();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ShuffleInLoop(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            e += Warp.ShuffleDownSync(FullMask, e, 1);
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SyncWarpInLoop(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncWarp();
            e += 1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SyncGridInLoop(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncGrid();
            e += 1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void RefReadOnlyInLoop(int[] r)
    {
        foreach (ref readonly int e in r.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncThreads();
            r[0] = e;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BarrierInANestedLoop(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            for (int i = 0; i < 2; i++)
            {
                Gpu.SyncThreads();
            }

            e += 1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SuspendingCallThroughASecondFunction(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            SuspendsThroughASecondFunction();
            e += 1;
        }
    }

    // Only the second loop has a split point
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BarrierInTheSecondLoopOnly(int[] r)
    {
        foreach (ref int first in r.AsSpan(ThreadIndex.X, 1))
        {
            first += 1;
        }

        foreach (ref int second in r.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncThreads();
            second += 1;
        }
    }

    // The inner loop has no split point; the barrier is in the outer one
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void OuterLoopWithAnInnerRefLoop(int[] r)
    {
        foreach (ref int outer in r.AsSpan(ThreadIndex.X, 1))
        {
            foreach (ref int inner in r.AsSpan(ThreadIndex.X, 1))
            {
                inner += 1;
            }

            Gpu.SyncThreads();
            outer += 1;
        }
    }

    // A plain foreach is not a ref loop
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void PlainLoopWithABarrier(int[] r)
    {
        foreach (int e in r.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncThreads();
            r[0] = e;
        }
    }

    // The barrier is in a local function of the loop: it is the local function's own, not the loop's
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BarrierInALocalFunctionOfTheLoop(int[] r)
    {
        foreach (ref int e in r.AsSpan(ThreadIndex.X, 1))
        {
            e += 1;

            void Inner()
            {
                Gpu.SyncThreads();
            }
        }
    }
}
