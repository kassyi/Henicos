using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A generic struct that holds a larger construction of itself (CS0523): G<int>, G<G<int>>, ... never repeat
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class GenericCyclicStruct
{
    public struct G<T>
    {
        public G<G<T>> Inner;
        public T X;
    }

    [GpuKernel]
    public static void GenericKernel(G<int> value, int[] output)
    {
        G<int> copy = value;
        output[0] = copy.X;
    }
}
