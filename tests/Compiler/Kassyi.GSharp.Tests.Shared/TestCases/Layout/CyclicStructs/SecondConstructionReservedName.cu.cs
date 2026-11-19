using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: G<int> holds arrays of G<Bad>, another construction that does not grow; Bad has a name reserved in C++
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class SecondConstructionReservedName
{
    public struct Bad
    {
        public int __x;
    }

    public struct G<T>
    {
        public G<Bad>[] K;
        public T Leaf;
    }

    [GpuKernel]
    public static void SecondConstructionKernel(G<int> value, int[] output)
    {
        output[0] = value.Leaf;
    }
}
