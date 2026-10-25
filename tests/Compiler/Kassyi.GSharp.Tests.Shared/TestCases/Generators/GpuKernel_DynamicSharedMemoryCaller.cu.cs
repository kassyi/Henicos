using Kassyi.GSharp.Core;

namespace TestNamespace.DynamicSharedMemoryCaching;

// The caller of GpuDevice_DynamicSharedMemoryCallee: its CUDA output depends on whether the callee declares dynamic shared
// memory (the table of regions and the hidden argument)
public static partial class DynamicSharedMemoryCachingKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void CallScratchKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        output[tid] = DynamicSharedMemoryHelpers.Scratch(n, tid);
    }
}
