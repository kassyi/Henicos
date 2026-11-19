using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: Root reaches Shared through 14 structs in its first field and directly in its second; Shared holds a
// multidimensional array, which the search must find however deep Shared was first met
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class DeepChainMultidimensional
{
    public struct Link1
    {
        public Link2 Next;
        public int X;
    }

    public struct Link2
    {
        public Link3 Next;
        public int X;
    }

    public struct Link3
    {
        public Link4 Next;
        public int X;
    }

    public struct Link4
    {
        public Link5 Next;
        public int X;
    }

    public struct Link5
    {
        public Link6 Next;
        public int X;
    }

    public struct Link6
    {
        public Link7 Next;
        public int X;
    }

    public struct Link7
    {
        public Link8 Next;
        public int X;
    }

    public struct Link8
    {
        public Link9 Next;
        public int X;
    }

    public struct Link9
    {
        public Link10 Next;
        public int X;
    }

    public struct Link10
    {
        public Link11 Next;
        public int X;
    }

    public struct Link11
    {
        public Link12 Next;
        public int X;
    }

    public struct Link12
    {
        public Link13 Next;
        public int X;
    }

    public struct Link13
    {
        public Link14 Next;
        public int X;
    }

    public struct Link14
    {
        public Shared Next;
        public int X;
    }

    public struct Shared
    {
        public Leaf Inner;
        public int X;
    }

    public struct Leaf
    {
        public int[,] Grid;
        public int X;
    }

    public struct Root
    {
        public Link1 First;
        public Shared Second;
    }

    [GpuKernel]
    public static void ChainKernel(Root root, int[] output)
    {
        output[0] = root.Second.X;
    }
}
