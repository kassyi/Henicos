#nullable enable
using System;
using System.Numerics;
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

    [GpuKernel]
    public static void InArrayAndReadOnlySpanFieldKernel(
        [In] int[] array,
        ReadOnlySpan<DiffDeepBag> bags,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            array[0] = 7;
            observed[0] = bags[0].Data[0];
        }
    }

    // --- [In] views of different element types: the CPU copy is shared bytes, as the device region is ---
    [GpuKernel]
    public static void InIntAndLongViewsKernel([In] Span<int> ints, [In] Span<long> longs, [Out] long[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            ints[2] += 8;
            longs[1] += 1L << 32;
            observed[0] = longs[0];
            observed[1] = ints[5];
        }
    }

    // --- [In] holders of different element types beside an [In] array: the CPU views the array's copy ---
    [GpuKernel]
    public static void InArrayAndLongMemoryKernel([In] int[] ints, [In] Memory<long> longs, [Out] long[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            ints[2] += 8;
            longs.Span[1] += 1L << 32;
            observed[0] = longs.Span[0];
            observed[1] = ints[5];
        }
    }

    [GpuKernel]
    public static void InArrayAndUintSegmentKernel(
        [In] int[] ints,
        [In] ArraySegment<uint> uints,
        [Out] long[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            ints[2] += 8;
            observed[0] = uints[1];
            uints[2] += 5u;
            observed[1] = ints[3];
        }
    }

    // --- Two T[] arguments that are one array, a slice of it, and a struct that puts the launch in table mode ---
    [GpuKernel]
    public static void TwoArraysSliceAndStructKernel(
        int[] first,
        int[] second,
        Span<int> slice,
        DiffDeepBag bag,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            slice[0] += 1;
            first[2] += 10;
            observed[0] = second[1] + bag.Tag;
            observed[1] = slice[1] + bag.Data[0];
        }
    }

    // --- Elements whose host and device layouts differ: the group is converted as a whole ---
    [GpuKernel]
    public static void ConvertedSlicesKernel(
        Span<DiffVec2Padded> a,
        Span<DiffVec2Padded> b,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            a[1].A += 10f;
            observed[0] = b[0].A;
            b[1].V = new Vector2(b[1].V.X + 1f, 2f);
            observed[1] = a[2].V.X;
        }
    }

    [GpuKernel]
    public static void InConvertedSlicesKernel(
        [In] Span<DiffVec2Padded> a,
        [In] Span<DiffVec2Padded> b,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            a[1].A = 7f;
            observed[0] = b[0].A;
        }
    }

    [GpuKernel]
    public static void TwoConvertedRefsKernel(
        ref DiffVec2Padded first,
        ref DiffVec2Padded second,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            first.A += 1f;
            second.A += 10f;
            observed[0] = first.A;
        }
    }

    [GpuKernel]
    public static void DeepBagSlicesKernel(Span<DiffDeepBag> a, Span<DiffDeepBag> b, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            a[1].Tag += 5;
            observed[0] = b[0].Tag;
            b[0].Data[0] += 100;
            observed[1] = a[1].Data[0];
        }
    }

    [GpuKernel]
    public static void RefAndSpanOfDeepBagsKernel(ref DiffDeepBag bag, Span<DiffDeepBag> bags, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Tag += 5;
            observed[0] = bags[1].Tag;
            bags[1].Data[0] += 100;
            observed[1] = bag.Data[0];
        }
    }

    // --- Two structs of two ints 4 bytes apart: the device aligns them to 4, so one region holds both ---
    [GpuKernel]
    public static void ShiftedPointsKernel(
        ref DiffOverlapPoint first,
        ref DiffOverlapPoint second,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            first.Y += 10;
            observed[0] = second.X;
            second.Y += 100;
            observed[1] = first.Y;
        }
    }
}
