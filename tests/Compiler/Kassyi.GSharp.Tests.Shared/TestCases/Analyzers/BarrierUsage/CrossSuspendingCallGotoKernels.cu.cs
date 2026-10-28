using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// The example of the GSH0040 page: a call to a device function that reaches a barrier counts as a barrier
public static partial class CrossSuspendingCallGotoKernels
{
    [GpuDeviceFunction]
    public static void Publish(int[] data, int value)
    {
        data[ThreadIndex.X] = value;
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void GotoAcrossSuspendingCallKernel(int[] data, int flag)
    {
        if (flag != 0)
        {
            goto Done;
        }

        Publish(data, 1);

    Done:
        data[ThreadIndex.X] += 1;
    }
}
