using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A struct that holds itself twice (CS0523 on both fields), as a kernel parameter and as a local
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class BranchingCyclicStruct
{
    public struct Tree
    {
        public Tree Left;
        public Tree Right;
        public int X;
    }

    [GpuKernel]
    public static void BranchingKernel(Tree value, int[] output)
    {
        Tree copy = value;
        output[0] = copy.X;
    }
}
