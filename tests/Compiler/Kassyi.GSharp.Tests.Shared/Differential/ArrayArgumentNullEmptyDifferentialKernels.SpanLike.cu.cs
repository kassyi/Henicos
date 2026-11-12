#nullable enable
using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// The span-like arguments: a default value is over no array, one over an empty array is not default
public static partial class ArrayArgumentNullEmptyDifferentialKernels
{
    [GpuKernel]
    public static void SpanKernel(Span<int> s, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = s == null ? 1 : 0;
                break;
            case ArrayArgumentReads.Length:
                o[0] = s.Length;
                break;
            case ArrayArgumentReads.ForEach:
                o[0] = 100;
                foreach (var v in s)
                {
                    o[0] += v + 1;
                }

                break;
        }
    }

    [GpuKernel]
    public static void MemoryKernel(Memory<int> m, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = m.Span == null ? 1 : 0;
                break;
            case ArrayArgumentReads.Length:
                o[0] = m.Length;
                break;
        }
    }

    [GpuKernel]
    public static void SegmentKernel(ArraySegment<int> g, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.Length:
                o[0] = g.Count;
                break;
        }
    }

    [GpuKernel]
    public static void ImmutableKernel(ImmutableArray<int> ia, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = ia.AsSpan() == null ? 1 : 0;
                break;
            case ArrayArgumentReads.Element:
                o[0] = ia[0];
                break;
            case ArrayArgumentReads.ForEach:
                o[0] = 100;
                foreach (var v in ia)
                {
                    o[0] += v + 1;
                }

                break;
        }
    }

    [GpuKernel]
    public static void NullableMemoryKernel(Memory<int>? m, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = (m.HasValue ? 1 : 0) + (m.HasValue && m.Value.Span == null ? 2 : 0);
                break;
            case ArrayArgumentReads.Length:
                o[0] = m.HasValue ? m.Value.Length : -1;
                break;
        }
    }
}
