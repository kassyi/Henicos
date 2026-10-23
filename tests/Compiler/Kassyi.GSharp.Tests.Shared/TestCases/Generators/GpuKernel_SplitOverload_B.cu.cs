using Kassyi.GSharp.Core;

namespace TestNamespace.SplitOverload;

// The overload of GpuKernel_SplitOverload_A.cu.cs, declared in another file of the same partial class
public static partial class SplitKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ScaleKernel(int[] data, int factor)
    {
        int i = ThreadIndex.X;
        data[i] = data[i] * factor;
    }
}
