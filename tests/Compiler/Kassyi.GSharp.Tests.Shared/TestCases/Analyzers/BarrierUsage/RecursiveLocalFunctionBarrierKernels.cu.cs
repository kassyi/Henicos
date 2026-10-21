using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class RecursiveLocalFunctionBarrierKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void RecursiveLocalFunctionBarrierKernel(int depth)
    {
        void RecursiveHelper(int d)
        {
            if (d <= 0) return;

            // GSH0045: Recursive local function containing barrier
            Gpu.SyncThreads();

            RecursiveHelper(d - 1);
        }

        RecursiveHelper(depth);
    }
}
