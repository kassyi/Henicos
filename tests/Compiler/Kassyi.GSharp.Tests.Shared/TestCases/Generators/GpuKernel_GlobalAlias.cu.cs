using Kassyi.GSharp.Core;

namespace TestNamespace.AliasKernels;

// A kernel marked through a global using alias of the attribute, declared in another file
public static partial class GlobalAliasKernels
{
    [K]
    [SkipSnapshotConsistency("Generator case")]
    public static void FillKernel(int[] data, int value)
    {
        data[ThreadIndex.X] = value;
    }
}
