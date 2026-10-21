using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class DivergentBarrierInDeviceFunctionKernels
{
    [GpuDeviceFunction]
    public static void WaitIfFirstHalf(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            // GSH0037: divergent barrier inside a device function
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void CallerKernel(int[] buffer)
    {
        WaitIfFirstHalf(buffer);
    }
}
