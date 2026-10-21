using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class ExternalBarrierCallKernels
{
    // GSH0042: Gpu.SyncThreads() called from external helper method not marked [GpuKernel]
    public static void ExternalHelperMethod()
    {
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void KernelCallingExternalHelper()
    {
        ExternalHelperMethod();
    }
}
