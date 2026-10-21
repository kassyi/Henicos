using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class FixedBlockBarrierKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public unsafe static void FixedBlockBarrierKernel(int[] managedArray)
    {
        fixed (int* ptr = &managedArray[0])
        {
            // GSH0043: Barrier inside fixed block
            Gpu.SyncThreads();
            ptr[0] = 1;
        }
    }
}
