using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class RefReturnArgumentKernels
{
    [GpuDeviceFunction]
    public static ref int Select(int[] buffer, int index) => ref buffer[index];

    [GpuDeviceFunction]
    public static void IncrementAcrossBarrier(ref int value)
    {
        value += 1;
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void RefReturnArgumentKernel(int[] buffer)
    {
        // GSH0062: the reference would be computed again on every resume
        IncrementAcrossBarrier(ref Select(buffer, ThreadIndex.X));
    }
}
