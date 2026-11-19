using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Layout;

/// <summary>A generic struct that names its own construction in a property, nested in itself by value.</summary>
public struct SwappableVector<T> where T : unmanaged
{
    public T X;
    public T Y;

    public SwappableVector<T> Swapped
    {
        get
        {
            SwappableVector<T> swapped;
            swapped.X = Y;
            swapped.Y = X;
            return swapped;
        }
    }
}

[SkipSnapshotConsistency("Value test of a nested generic construction; CyclicStructLayoutTests checks its layout")]
public static partial class NestedGenericConstructionKernels
{
    [GpuKernel]
    public static void NestedConstructionKernel(SwappableVector<SwappableVector<int>>[] values, int[] output)
    {
        int i = ThreadIndex.X;
        output[i] = values[i].Y.X * 10 + values[i].Y.Y;
    }
}
