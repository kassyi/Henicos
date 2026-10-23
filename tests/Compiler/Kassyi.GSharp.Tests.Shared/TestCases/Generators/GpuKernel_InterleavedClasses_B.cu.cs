using Kassyi.GSharp.Core;

namespace TestNamespace.Interleaved;

// Another class, between the two parts of OuterKernels
public static partial class MiddleKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void MiddleKernel(int[] data) => data[ThreadIndex.X] = 2;
}
