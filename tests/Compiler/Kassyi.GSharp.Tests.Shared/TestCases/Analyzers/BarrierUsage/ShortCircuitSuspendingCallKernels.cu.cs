using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class ShortCircuitSuspendingCallKernels
{
    [GpuDeviceFunction]
    public static bool WaitAndConfirm()
    {
        Gpu.SyncThreads();
        return true;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ShortCircuitCallKernel(int[] buffer)
    {
        // GSH0037: the right operand runs only for the threads whose left operand is true
        bool done = ThreadIndex.X < 16 && WaitAndConfirm();
        buffer[ThreadIndex.X] = done ? 1 : 0;

        // The threads that skip the barrier above meet the others here
        Gpu.SyncThreads();
    }
}
