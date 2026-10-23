using Kassyi.GSharp.Core;

namespace TestNamespace.Interleaved;

// The second part of OuterKernels
public static partial class OuterKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void LastKernel(int[] data) => data[ThreadIndex.X] = 3;
}
