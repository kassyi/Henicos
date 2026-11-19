#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public struct SuspendingAccessHolder
{
    public int[]? Data;
}

public struct SuspendingAccessPoint
{
    public int X;
}

public static partial class SuspendingCallAfterConditionalAccessKernels
{
    [GpuDeviceFunction]
    public static int Step(ref int evaluations)
    {
        Gpu.SyncThreads();
        evaluations++;
        return 1;
    }

    [GpuDeviceFunction]
    public static int Plain(ref int evaluations)
    {
        evaluations++;
        return 1;
    }

    [GpuDeviceFunction]
    public static SuspendingAccessPoint? PickPoint(int mode)
    {
        Gpu.SyncThreads();
        return mode == 0 ? null : new SuspendingAccessPoint { X = 7 };
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void IndexKernel(int[] a, int[] dst, int mode)
    {
        int[]? src = mode == 0 ? null : a;
        int e = 0;
        // GSH0062: the index is evaluated only for a non-null receiver
        int? v = src?[Step(ref e)];
        dst[ThreadIndex.X] = (v ?? -1) + e;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void AssignmentKernel(int[] a, int[] dst, int mode)
    {
        int[]? target = mode == 0 ? null : dst;
        int e = 0;
        // GSH0062: the right side, and the index, are evaluated only for a non-null receiver
        target?[ThreadIndex.X] = 7 + Step(ref e);
        target?[Step(ref e)] += 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ChainKernel(int[] a, int[] dst, int mode)
    {
        SuspendingAccessHolder? holder = mode == 0 ? null : new SuspendingAccessHolder { Data = a };
        int e = 0;
        // GSH0062: the inner access is after the outer '?.' as well
        int? first = holder?.Data?[Step(ref e)];
        dst[ThreadIndex.X] = (first ?? -1) + e;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void NestedInOtherOperatorsKernel(int[] a, int[] dst, int mode)
    {
        int[]? src = mode == 0 ? null : a;
        int e = 0;
        // GSH0062 twice: after '?.' inside '?:', and as the right operand of '??'
        int v = mode == 2 ? (src?[Step(ref e)] ?? -1) : (src?[0] ?? Step(ref e));
        dst[ThreadIndex.X] = v + e;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void AcceptedFormsKernel(int[] a, int[] dst, int mode)
    {
        int[]? src = mode == 0 ? null : a;
        int e = 0;
        // No diagnostic: the access holds no suspending call
        int? plain = src?[Plain(ref e)];
        // No diagnostic: the receiver suspends, before the null test
        int? x = PickPoint(mode)?.X;
        // No diagnostic: the suspending call is an operand next to the access, not inside it
        int next = (src?[Plain(ref e)] ?? -1) + Step(ref e);
        int before = Step(ref e) + (src?.Length ?? -1);
        // No diagnostic: an operand of '?:' is split
        int chosen = mode == 2 ? Step(ref e) : (src?[0] ?? -1);
        dst[ThreadIndex.X] = (plain ?? -1) + (x ?? -1) + next + before + chosen + e;
    }
}
