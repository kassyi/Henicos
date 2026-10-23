using Kassyi.GSharp.Core;

namespace TestNamespace.SplitOverload;

// One overload of a kernel; GpuKernel_SplitOverload_B.cu.cs adds the other overload to the same partial class
public static partial class SplitKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ScaleKernel(float[] data, float factor)
    {
        int i = ThreadIndex.X;
        data[i] = data[i] * factor;
    }
}
