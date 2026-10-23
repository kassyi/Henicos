using Kassyi.GSharp.Core;

namespace TestNamespace.TwoClasses;

// Two kernel classes; a test edits the first one's kernel and checks that the second one's wrapper stays cached
public static partial class FirstKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void FirstKernel(float[] data)
    {
        int i = ThreadIndex.X;
        data[i] = data[i] * 2f;
    }
}

public static partial class SecondKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void SecondKernel(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] = data[i] + 1;
    }
}
