using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SuspendingCallInSwitchExpressionKernels
{
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SwitchExpressionKernel(int[] buffer, int mode)
    {
        // GSH0064: only the selected arm runs, and the arms are not lowered into branches
        buffer[ThreadIndex.X] = mode switch
        {
            0 => Broadcast(buffer[ThreadIndex.X]),
            _ => buffer[ThreadIndex.X]
        };
    }
}
