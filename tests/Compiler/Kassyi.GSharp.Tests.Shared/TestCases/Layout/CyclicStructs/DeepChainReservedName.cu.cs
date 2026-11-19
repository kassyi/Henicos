using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: Root reaches Shared through 7 structs in its first field and directly in its second; Shared holds a
// member with a name reserved in C++, which the search must find however deep Shared was first met
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class DeepChainReservedName
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
        public int __bad;
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
