using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class RecursiveDeviceFunctionBarrierKernels
{
    // GSH0063: a device function that reaches a barrier calls itself, so its state machine frame has no static size
    [GpuDeviceFunction]
    public static void DirectlyRecursiveBarrier(int depth)
    {
        if (depth <= 0) return;
        Gpu.SyncThreads();
        DirectlyRecursiveBarrier(depth - 1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void DirectRecursionKernel(int depth)
    {
        DirectlyRecursiveBarrier(depth);
    }
}
