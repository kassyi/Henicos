using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SuspendingCallInLocalFunctionKernels
{
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LocalFunctionKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = BroadcastTwice(buffer[ThreadIndex.X]);

        // GSH0062: a local function body is not part of the kernel state machine
        static int BroadcastTwice(int value) => Broadcast(Broadcast(value));
    }
}
