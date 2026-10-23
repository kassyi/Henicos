using Kassyi.GSharp.Core;

namespace TestNamespace.Interleaved;

// The first part of a class whose kernels are split by another class's file (GpuKernel_InterleavedClasses_B.cu.cs)
public static partial class OuterKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void FirstKernel(int[] data) => data[ThreadIndex.X] = 1;
}
