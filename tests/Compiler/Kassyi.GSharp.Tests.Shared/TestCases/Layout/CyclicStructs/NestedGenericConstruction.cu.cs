using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: V<T> names V<T> in a property, Tr<T> in an array; V<V<int>> and Tr<Tr<int>> hold another construction
// by value without a cycle
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class NestedGenericConstruction
{
    public struct V<T> where T : unmanaged
    {
        public T X;
        public T Y;

        public V<T> Swapped => default;
    }

    public struct Tr<T>
    {
        public Tr<T>[] Kids;
        public T Value;
    }

    [GpuKernel]
    public static void NestedConstructionKernel(V<V<int>>[] values, int[] output)
    {
        output[0] = values[0].Y.X;
    }
}
