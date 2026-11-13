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
            case ArrayArgumentReads.Length:
                o[0] = ia.Length;
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

    [GpuKernel]
    public static void ReadOnlySpanKernel(ReadOnlySpan<int> s, int mode, int[] o)
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
            case ArrayArgumentReads.InNullableFunction:
                o[0] = SpanSum(s);
                break;
        }
    }

    [GpuKernel]
    public static void ReadOnlyMemoryKernel(ReadOnlyMemory<int> m, int mode, int[] o)
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

    // HasValue, then the count of the value: a default segment counts 0
    [GpuKernel]
    public static void NullableSegmentKernel(ArraySegment<int>? g, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = g.HasValue ? g.Value.Count : -1;
    }

    // HasValue, then the length of the value: Length of a default ImmutableArray<T> throws NullReferenceException
    [GpuKernel]
    public static void NullableImmutableKernel(ImmutableArray<int>? ia, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = ia.HasValue ? ia.Value.Length : -1;
    }

    // Two nullable memory arguments, each with its own HasValue
    [GpuKernel]
    public static void TwoNullableMemoryKernel(Memory<int>? a, Memory<int>? b, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = (a.HasValue ? a.Value.Length : -1) * 10 + (b.HasValue ? b.Value.Length : -1);
    }

    // A span passed to a device function, which reads it with foreach and Length (nothing for a default or empty span)
    [GpuDeviceFunction]
    public static int SpanSum(ReadOnlySpan<int> s)
    {
        var sum = 1000;
        foreach (var v in s)
        {
            sum += v;
        }

        return sum + s.Length;
    }
}
