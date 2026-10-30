using Kassyi.GSharp.Core;

namespace TestNamespace.ManagedVersionCaching;

// The caller of GpuDevice_ManagedVersionCallee: its managed state machine calls the managed version of Sum once Sum has
// one
public static partial class ManagedVersionCachingKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void CallSumKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        output[tid] = ManagedVersionHelpers.Sum(tid, n);
    }
}
