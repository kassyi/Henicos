using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Two structs that hold each other (CS0523 on both fields) as a kernel parameter
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class MutualCyclicStructs
{
    public struct First
    {
        public Second Other;
        public int X;
    }

    public struct Second
    {
        public First Other;
        public int Y;
    }

    [GpuKernel]
    public static void MutualKernel(First value, int[] output)
    {
        output[0] = value.X;
    }
}
