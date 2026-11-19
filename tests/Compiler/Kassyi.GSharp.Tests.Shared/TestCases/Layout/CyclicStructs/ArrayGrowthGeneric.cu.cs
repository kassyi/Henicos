using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Valid C#: N<int> holds arrays of N<int[]>, which holds arrays of N<int[][]>, ...: no constructed type repeats
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class ArrayGrowthGeneric
{
    public struct N<T>
    {
        public N<T[]>[] Kids;
        public T X;
    }

    [GpuKernel]
    public static void GrowthKernel(N<int> value, N<int>[] values, int[] output)
    {
        output[0] = value.X + values[0].X;
    }
}
