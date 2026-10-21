using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class MutuallyRecursiveDeviceFunctionBarrierKernels
{
    // GSH0063: the cycle Ping -> Pong -> Ping reaches a barrier
    [GpuDeviceFunction]
    public static void Ping(int depth)
    {
        if (depth <= 0) return;
        Pong(depth - 1);
    }

    [GpuDeviceFunction]
    public static void Pong(int depth)
    {
        Gpu.SyncThreads();
        Ping(depth);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void MutualRecursionKernel(int depth)
    {
        Ping(depth);
    }
}
