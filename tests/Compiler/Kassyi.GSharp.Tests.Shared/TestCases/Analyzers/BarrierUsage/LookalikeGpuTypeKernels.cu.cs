using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class LookalikeGpuTypeKernels
{
    // A user's own type that only shares its name with Kassyi.GSharp.Core.Gpu
    public static class Lookalike
    {
        public static class Gpu
        {
            public static void SyncThreads()
            {
            }
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UserSyncThreadsInBranchKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            // Not a barrier: no GSH0037
            Lookalike.Gpu.SyncThreads();
        }
    }
}
