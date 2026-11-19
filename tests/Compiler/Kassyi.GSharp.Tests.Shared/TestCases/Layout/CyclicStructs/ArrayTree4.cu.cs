using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: a struct with four array fields of its own type (no cycle by value) as a kernel parameter
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class ArrayTree4
{
    public struct Node
    {
        public Node[] F0;
        public Node[] F1;
        public Node[] F2;
        public Node[] F3;
        public int X;
    }

    [GpuKernel]
    public static void ArrayTreeKernel(Node value, int[] output)
    {
        output[0] = value.X;
    }
}
