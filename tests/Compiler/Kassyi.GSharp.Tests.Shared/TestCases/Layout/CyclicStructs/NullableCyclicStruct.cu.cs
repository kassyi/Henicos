using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A struct that holds a Nullable of itself (CS0523) as a kernel parameter and as a local
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class NullableCyclicStruct
{
    public struct Node
    {
        public Node? Inner;
        public int X;
    }

    [GpuKernel]
    public static void NullableKernel(Node value, int[] output)
    {
        Node copy = value;
        output[0] = copy.X;
    }
}
