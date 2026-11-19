using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: G<int> holds arrays of G<Other.Wrap>, another construction that does not grow; Wrap holds a field of a
// private type that the launcher cannot name
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class SecondConstructionInaccessible
{
    public struct G<T>
    {
        public G<Other.Wrap>[] K;
        public T Leaf;
    }

    [GpuKernel]
    public static void SecondConstructionKernel(G<int> value, int[] output)
    {
        output[0] = value.Leaf;
    }
}

public class Other
{
    private struct Hidden
    {
        public int V;
    }

    public struct Wrap
    {
        private Hidden H;
        public int X;
    }
}
