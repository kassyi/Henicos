using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class DivergentSuspendingCallKernels
{
    [GpuDeviceFunction]
    public static void WaitForBlock()
    {
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void DivergentCallKernel(int[] buffer)
    {
        if (ThreadIndex.X == 0)
        {
            // GSH0083: the callee reaches a block barrier that only some threads arrive at
            WaitForBlock();
        }

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }
}
