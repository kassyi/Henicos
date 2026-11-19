using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A struct that holds a cyclic struct with an array field, as a kernel parameter and as an array element
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class NestedCyclicStruct
{
    public struct Cyc
    {
        public Cyc Inner;
        public int[] Values;
    }

    public struct Outer
    {
        public Cyc Held;
        public int X;
    }

    [GpuKernel]
    public static void NestedKernel(Outer value, Cyc[] items, int[] output)
    {
        output[0] = value.X + items.Length;
    }
}
