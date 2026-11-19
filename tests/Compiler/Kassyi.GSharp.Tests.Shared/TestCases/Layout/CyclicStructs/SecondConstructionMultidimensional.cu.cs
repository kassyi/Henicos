using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: G<int> holds arrays of G<M>, another construction that does not grow; M holds a multidimensional array
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class SecondConstructionMultidimensional
{
    public struct M
    {
        public int[,] A;
    }

    public struct G<T>
    {
        public G<M>[] K;
        public T Leaf;
    }

    [GpuKernel]
    public static void SecondConstructionKernel(G<int> value, int[] output)
    {
        output[0] = value.Leaf;
    }
}
