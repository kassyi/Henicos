using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda.Syntax;

[SkipSnapshotConsistency]
public static partial class SpanSortKernels
{
    // Span<T>.Sort() lowers to thrust::sort, so the AOT build has to compile the thrust headers
    [GpuKernel]
    public static void SortSpanKernel(Span<int> data)
    {
        if (ThreadIndex.X == 0)
        {
            data.Sort();
        }
    }
}
