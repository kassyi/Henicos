using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class DivergentNestedSuspendingCallKernels
{
    [GpuDeviceFunction]
    public static void WaitForBlock()
    {
        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void WaitThroughHelper()
    {
        WaitForBlock();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void DivergentNestedCallKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            // GSH0037: the barrier is two calls deep
            WaitThroughHelper();
        }
    }
}
