#nullable enable
using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// The span-like arguments: a default value is over no array, one over an empty array is not default
public static partial class ArrayArgumentNullEmptyDifferentialKernels
{
    [GpuKernel]
    public static void SpanKernel(Span<int> s, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = s == null ? 1 : 0;
                break;
            case ArrayArgumentRead.Length:
                o[0] = s.Length;
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in s)
                {
                    o[0] += v + 1;
                }

                break;
        }
    }

    [GpuKernel]
    public static void MemoryKernel(Memory<int> m, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = m.Span == null ? 1 : 0;
                break;
            case ArrayArgumentRead.Length:
                o[0] = m.Length;
                break;
            case ArrayArgumentRead.IsEmpty:
                o[0] = m.IsEmpty ? 1 : 0;
                break;
        }
    }

    [GpuKernel]
    public static void SegmentKernel(ArraySegment<int> g, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.Length:
                o[0] = g.Count;
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in g)
                {
                    o[0] += v + 1;
                }

                break;
        }
    }

    // A barrier in the body lowers the foreach into an indexed loop over the segment, which the CPU paths run in their
    // state machine; a default segment throws at the loop entry as in the plain loop
    [GpuKernel]
    public static void SegmentForEachBarrierKernel(ArraySegment<int> g, int[] o)
    {
        o[0] = 100;
        foreach (var v in g)
        {
            Gpu.SyncThreads();
            o[0] += v + 1;
        }
    }

    // The same lowered loop over an ImmutableArray<T>: a default one has no array and throws NullReferenceException at
    // the loop entry, which the source check keeps in the CPU state machine
    [GpuKernel]
    public static void ImmutableForEachBarrierKernel(ImmutableArray<int> g, int[] o)
    {
        o[0] = 100;
        foreach (var v in g)
        {
            Gpu.SyncThreads();
            o[0] += v + 1;
        }
    }

    [GpuKernel]
    public static void ImmutableKernel(ImmutableArray<int> ia, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = ia.AsSpan() == null ? 1 : 0;
                break;
            case ArrayArgumentRead.Length:
                o[0] = ia.Length;
                break;
            case ArrayArgumentRead.Element:
                o[0] = ia[0];
                break;
            case ArrayArgumentRead.IsEmpty:
                o[0] = ia.IsEmpty ? 1 : 0;
                break;
            case ArrayArgumentRead.IsDefault:
                o[0] = ia.IsDefault ? 1 : 0;
                break;
            case ArrayArgumentRead.IsDefaultOrEmpty:
                o[0] = ia.IsDefaultOrEmpty ? 1 : 0;
                break;
            case ArrayArgumentRead.DefaultLiteral:
                o[0] = (default(ImmutableArray<int>).IsDefault ? 1 : 0) +
                    (default(ImmutableArray<int>).IsDefaultOrEmpty ? 2 : 0);
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in ia)
                {
                    o[0] += v + 1;
                }

                break;
        }
    }

    [GpuKernel]
    public static void NullableMemoryKernel(Memory<int>? m, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = (m.HasValue ? 1 : 0) + (m.HasValue && m.Value.Span == null ? 2 : 0);
                break;
            case ArrayArgumentRead.Length:
                o[0] = m.HasValue ? m.Value.Length : -1;
                break;
        }
    }

    [GpuKernel]
    public static void ReadOnlySpanKernel(ReadOnlySpan<int> s, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = s == null ? 1 : 0;
                break;
            case ArrayArgumentRead.Length:
                o[0] = s.Length;
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in s)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentRead.InNullableFunction:
                o[0] = SpanSum(s);
                break;
        }
    }

    [GpuKernel]
    public static void ReadOnlyMemoryKernel(ReadOnlyMemory<int> m, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = m.Span == null ? 1 : 0;
                break;
            case ArrayArgumentRead.Length:
                o[0] = m.Length;
                break;
            case ArrayArgumentRead.IsEmpty:
                o[0] = m.IsEmpty ? 1 : 0;
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
