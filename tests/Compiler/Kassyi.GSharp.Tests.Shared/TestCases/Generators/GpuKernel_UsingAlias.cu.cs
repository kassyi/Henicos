using Kassyi.GSharp.Core;
using K = Kassyi.GSharp.Core.GpuKernelAttribute;

namespace TestNamespace.AliasKernels;

// A kernel marked through a using alias of the attribute
public static partial class UsingAliasKernels
{
    [K]
    [SkipSnapshotConsistency("Generator case")]
    public static void FillKernel(int[] data, int value)
    {
        data[ThreadIndex.X] = value;
    }
}
