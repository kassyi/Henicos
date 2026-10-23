using Kassyi.GSharp.Core;

namespace TestNamespace.PartialKernels;

// A partial kernel whose parts name the parameters differently; the attribute is on the implementing part
public static partial class PartialImplementationKernels
{
    public static partial void FillKernel(int[] data, int value);

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static partial void FillKernel(int[] values, int fill)
    {
        values[ThreadIndex.X] = fill;
    }
}
