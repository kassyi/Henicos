using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static partial class ArrayLikeMultidimensionalField
{
    public struct SegmentHolder
    {
        public ArraySegment<int[,]> Seg;
    }

    public struct ImmutableHolder
    {
        public ImmutableArray<int[,]> Items;
    }

    [GpuKernel]
    public static void HolderKernel(SegmentHolder segment, ImmutableHolder immutable, int[] output)
    {
        output[0] = 0;
    }
}
