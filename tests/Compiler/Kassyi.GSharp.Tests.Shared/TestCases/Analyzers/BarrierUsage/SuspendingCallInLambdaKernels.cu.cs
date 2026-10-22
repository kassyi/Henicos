using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SuspendingCallInLambdaKernels
{
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LambdaKernel(int[] buffer)
    {
        // GSH0062: a lambda body is not part of the kernel state machine
        Func<int, int> broadcast = value => Broadcast(value);
        buffer[ThreadIndex.X] = broadcast(buffer[ThreadIndex.X]);
    }
}
