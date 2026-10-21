using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class CrossSuspendingCallRefStructKernels
{
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuDeviceFunction]
    public static void BroadcastIntoSpan(int[] buffer)
    {
        Span<int> view = buffer;
        // GSH0039: the span lives across the suspending call and is used afterwards
        int broadcast = Broadcast(view[ThreadIndex.X]);
        view[ThreadIndex.X] = broadcast;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void CallerKernel(int[] buffer)
    {
        BroadcastIntoSpan(buffer);
    }
}
