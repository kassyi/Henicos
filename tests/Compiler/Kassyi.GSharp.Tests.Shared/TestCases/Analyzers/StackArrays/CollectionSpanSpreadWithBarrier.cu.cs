using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.StackArrays;

// GSH0003 for a spread element inside a collection expression initializing a span in a kernel with a barrier
[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static partial class CollectionSpanSpreadWithBarrierKernels
{
    [GpuKernel]
    public static void SpreadSpanBeforeBarrierKernel(int[] data, int[] a)
    {
        Span<int> s = [.. data];
        a[0] = s[0];
        Gpu.SyncThreads();
        a[1] = 2;
    }
}
