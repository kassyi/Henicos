#nullable enable
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct that holds an array of elements whose host and device layouts differ.</summary>
public struct DiffPaddedHolder
{
    public DiffVec2Padded[] Items;
}

/// <summary>
/// Kernels that receive an array through a field of a struct argument and a slice of the same array through a span
/// argument: one memory in C#, so a write through the field is read through the slice and the other way. Each kernel
/// passes the struct in one form (by value, <c>in</c>, <c>ref</c>, <c>out</c>, nullable, an element of an array or of a
/// read-only container).
/// </summary>
public static partial class MarshalingFieldOverlapDifferentialKernels
{
    // --- An array that a struct argument holds, and a slice of the same array ---
    [GpuKernel]
    public static void FieldAndSliceKernel(DiffDeepBag bag, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bag.Data[2];
        }
    }

    // The struct is an element of an array argument
    [GpuKernel]
    public static void ElementFieldAndSliceKernel(DiffDeepBag[] bags, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bags[0].Data[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bags[0].Data[2];
        }
    }

    // The struct is passed by ref, and the slice is a read-only one
    [GpuKernel]
    public static void RefFieldAndReadOnlySliceKernel(
        ref DiffDeepBag bag,
        ReadOnlySpan<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[1] += 10;
            observed[0] = slice[0];
        }
    }

    // Both arguments are [In]: the CPU gives each a copy, which must be one copy of the one array
    [GpuKernel]
    public static void InFieldAndInSliceKernel([In] DiffDeepBag[] bags, [In] Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bags[0].Data[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bags[0].Data[2];
        }
    }

    // The elements of the array are converted between the host and the device layout
    [GpuKernel]
    public static void ConvertedFieldAndSliceKernel(
        DiffPaddedHolder holder,
        Span<DiffVec2Padded> slice,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            holder.Items[1].A += 10f;
            observed[0] = slice[0].A;
            slice[1].A += 100f;
            observed[1] = holder.Items[2].A;
        }
    }

    // The struct is passed as an in parameter, which the launcher reads through Unsafe.AsRef
    [GpuKernel]
    public static void InStructAndSliceKernel(in DiffDeepBag bag, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bag.Data[2];
        }
    }

    // The struct is a Nullable<DiffDeepBag>
    [GpuKernel]
    public static void NullableStructAndSliceKernel(DiffDeepBag? bag, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            var held = bag.GetValueOrDefault();
            held.Data[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = held.Data[2];
        }
    }

    // The struct is an out parameter whose old value held the array that the slice views: the launcher clears the
    // variable before the kernel runs, so the old arrays are no memory of this launch
    [GpuKernel]
    public static void OutStructAndSliceKernel(out DiffDeepBag bag, Span<int> slice, [Out] int[] observed)
    {
        bag = default;
        if (ThreadIndex.X == 0)
        {
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bag.Tag;
        }
    }

    // The structs are the elements of a read-only span
    [GpuKernel]
    public static void ReadOnlySpanElementsAndSliceKernel(
        ReadOnlySpan<DiffDeepBag> bags,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            observed[0] = bags[0].Data[1];
            slice[1] += 100;
            observed[1] = bags[0].Data[2];
        }
    }

    // The structs are the elements of an ImmutableArray
    [GpuKernel]
    public static void ImmutableArrayElementsAndSliceKernel(
        ImmutableArray<DiffDeepBag> bags,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            observed[0] = bags[0].Data[1];
            slice[1] += 100;
            observed[1] = bags[0].Data[2];
        }
    }

    // The structs are the elements of a ReadOnlyMemory
    [GpuKernel]
    public static void ReadOnlyMemoryElementsAndSliceKernel(
        ReadOnlyMemory<DiffDeepBag> bags,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            observed[0] = bags.Span[0].Data[1];
            slice[1] += 100;
            observed[1] = bags.Span[0].Data[2];
        }
    }
}
