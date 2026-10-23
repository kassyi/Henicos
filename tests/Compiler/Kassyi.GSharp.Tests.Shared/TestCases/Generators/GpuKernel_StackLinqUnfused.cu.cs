using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace TestNamespace.StackLinqUnfused;

// Chains the fusion rule does not handle (ElementAt is not a fused terminal), and MemoryExtensions.Sort. Both are emitted as
// library calls (Kassyi::ZeroAlloc::Linq::StackLinq::*, thrust::sort), not as inlined loops.
public static partial class StackLinqUnfusedKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ElementAtKernel(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0)
        {
            result[0] = data.AsStackEnumerable()
                .ElementAt(1);
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void SortKernel(Span<int> data)
    {
        data.Sort();
    }
}
