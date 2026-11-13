#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct whose host and device layouts are the same, passed by reference twice.</summary>
public struct DiffOverlapPoint
{
    public int X;
    public int Y;
}

/// <summary>
/// Kernels that receive overlapping memory through two or more parameters: slices of one array, a span and its array, a
/// <c>ref</c> to an element and its array, views of one array with different element types, one variable twice.
/// Each one writes through one parameter and reads through another, so a launcher that copies the parameters apart
/// gives a different value.
/// </summary>
public static partial class MarshalingOverlapDifferentialKernels
{
    [GpuKernel]
    public static void SpanAndReadOnlySpanKernel(Span<int> written, ReadOnlySpan<int> read, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            written[0] += 10;
            observed[0] = read[0];
        }
    }

    [GpuKernel]
    public static void RefElementAndArrayKernel(ref int element, int[] array, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            element += 10;
            observed[0] = array[0];
            array[0] += 100;
            observed[1] = element;
        }
    }

    [GpuKernel]
    public static void InSpanAndSpanKernel([In] Span<int> readOnly, Span<int> written, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            written[0] += 10;
            observed[0] = readOnly[1];
        }
    }

    [GpuKernel]
    public static void TwoInSpansKernel([In] Span<int> first, [In] Span<int> second, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            first[1] += 10;
            observed[0] = second[0];
        }
    }

    [GpuKernel]
    public static void InSpanAndOutArrayKernel([In] Span<int> readOnly, [Out] int[] written, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            observed[0] = readOnly[0];
            written[1] = 77;
            observed[1] = readOnly[1];
        }
    }

    [GpuKernel]
    public static void ArrayAndSliceKernel(int[] array, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            slice[0] += 10;
            observed[0] = array[1];
            array[2] += 100;
            observed[1] = slice[1];
        }
    }

    [GpuKernel]
    public static void ThreeChainedSlicesKernel(Span<int> a, Span<int> b, Span<int> c, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            a[2] += 10;
            observed[0] = b[0];
            b[2] += 100;
            observed[1] = c[0];
            c[2] += 1000;
        }
    }

    [GpuKernel]
    public static void MemoryAndSegmentKernel(Memory<int> memory, ArraySegment<int> segment, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            memory.Span[1] += 10;
            observed[0] = segment[0];
            segment[1] += 100;
            observed[1] = memory.Span[2];
        }
    }

    [GpuKernel]
    public static void IntAndLongViewsKernel(Span<int> ints, Span<long> longs, [Out] long[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            ints[2] += 5;
            observed[0] = longs[0];
            longs[1] += 1L << 32;
            observed[1] = ints[5];
        }
    }

    [GpuKernel]
    public static void TwoRefStructsKernel(
        ref DiffOverlapPoint first,
        ref DiffOverlapPoint second,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            first.X += 10;
            second.Y += first.X;
            observed[0] = first.Y;
        }
    }

    [GpuKernel]
    public static void AdjacentSlicesKernel(Span<int> left, Span<int> right, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            left[1] += 10;
            right[0] += 100;
            observed[0] = left[1];
            observed[1] = right[0];
        }
    }

    // --- [In] holders that share one array (#985): a write through one is seen through the other ---
    [GpuKernel]
    public static void InArrayAndInFieldKernel([In] int[] array, [In] DiffDeepBag[] bags, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            array[0] = 7;
            observed[0] = bags[0].Data[0];
        }
    }

    [GpuKernel]
    public static void TwoInFieldsKernel([In] DiffArrayPair[] pairs, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            pairs[0].First[0] = 9;
            observed[0] = pairs[0].Second[0];
        }
    }
}
