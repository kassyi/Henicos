#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct that holds an array of elements whose host and device layouts differ.</summary>
public struct DiffPaddedHolder
{
    public DiffVec2Padded[] Items;
}

/// <summary>A struct that holds a writable <see cref="Memory{T}"/> of elements whose host and device layouts differ.</summary>
public struct DiffPaddedMemoryHolder
{
    public Memory<DiffVec2Padded> Items;
}

/// <summary>A struct that holds a writable <see cref="Memory{T}"/> view.</summary>
public struct DiffMemoryBag
{
    public Memory<int> Data;
    public int Tag;
}

/// <summary>A struct that holds an <see cref="ArraySegment{T}"/> view.</summary>
public struct DiffSegmentBag
{
    public ArraySegment<int> Data;
    public int Tag;
}

/// <summary>A struct that holds a <see cref="ReadOnlyMemory{T}"/> view.</summary>
public struct DiffReadOnlyMemoryBag
{
    public ReadOnlyMemory<int> Data;
    public int Tag;
}

/// <summary>A struct that holds an <see cref="ImmutableArray{T}"/> view.</summary>
public struct DiffImmutableArrayBag
{
    public ImmutableArray<int> Data;
    public int Tag;
}

/// <summary>A struct that holds two <see cref="Memory{T}"/> views.</summary>
public struct DiffTwoMemoryViews
{
    public Memory<int> A;
    public Memory<int> B;
}

/// <summary>A struct that holds a nullable <see cref="Memory{T}"/> view.</summary>
public struct DiffNullableMemoryBag
{
    public Memory<int>? Data;
    public int Tag;
}

/// <summary>A struct that nests <see cref="DiffMemoryBag"/>.</summary>
public struct DiffNestedMemoryBag
{
    public DiffMemoryBag Inner;
    public int Scale;
}

/// <summary>A struct that holds a writable <see cref="Memory{T}"/> of <see cref="short"/>.</summary>
public struct DiffShortMemoryBag
{
    public Memory<short> Data;
    public int Tag;
}

/// <summary>A struct that holds a writable <see cref="Memory{T}"/> of <see cref="Vector2"/>.</summary>
public struct DiffVector2MemoryBag
{
    public Memory<Vector2> Data;
}

/// <summary>A struct that holds an array of <see cref="Vector2"/>.</summary>
public struct DiffVector2ArrayBag
{
    public Vector2[] Data;
}

/// <summary>A struct that holds a <see cref="ReadOnlyMemory{T}"/> of <see cref="DiffMemoryBag"/>.</summary>
public struct DiffReadOnlyMemoryOfBagsHolder
{
    public ReadOnlyMemory<DiffMemoryBag> Bags;
    public int Tag;
}

/// <summary>A struct that holds an <see cref="ImmutableArray{T}"/> of <see cref="DiffMemoryBag"/>.</summary>
public struct DiffImmutableArrayOfBagsHolder
{
    public ImmutableArray<DiffMemoryBag> Bags;
    public int Tag;
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

    // The out struct held an array of converted elements that the slice views: the old arrays are no memory of this
    // launch, so the slice is no member of a group that CUDA cannot give converted elements
    [GpuKernel]
    public static void OutConvertedStructAndSliceKernel(
        out DiffPaddedHolder holder,
        Span<DiffVec2Padded> slice,
        [Out] float[] observed)
    {
        holder = default;
        if (ThreadIndex.X == 0)
        {
            observed[0] = slice[0].A;
            slice[1].A += 100f;
            observed[1] = slice[1].A;
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

    // --- A view that a struct argument holds, and an overlapping slice or view ---

    [GpuKernel]
    public static void MemoryFieldAndSliceKernel(DiffMemoryBag bag, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bag.Data.Span[2];
        }
    }

    [GpuKernel]
    public static void SegmentFieldAndSliceKernel(DiffSegmentBag bag, Span<int> slice, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bag.Data[2];
        }
    }

    [GpuKernel]
    public static void ReadOnlyMemoryFieldAndSliceKernel(
        DiffReadOnlyMemoryBag bag,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            slice[0] += 10;
            observed[0] = bag.Data.Span[1];
        }
    }

    [GpuKernel]
    public static void ImmutableArrayFieldAndReadOnlySliceKernel(
        DiffImmutableArrayBag bag,
        ReadOnlySpan<int> slice,
        Span<int> writer,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            writer[0] += 10;
            observed[0] = bag.Data[1];
            observed[1] = slice[0];
        }
    }

    [GpuKernel]
    public static void TwoMemoryFieldsKernel(DiffTwoMemoryViews h, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            h.A.Span[2] += 10;
            observed[0] = h.B.Span[1];
            h.B.Span[1] += 100;
            observed[1] = h.A.Span[2];
        }
    }

    [GpuKernel]
    public static void StructWithTwoViewsAloneKernel(DiffTwoMemoryViews h)
    {
        if (ThreadIndex.X == 0)
        {
            h.A.Span[1] += 10;
            h.B.Span[1] = h.B.Span[0] + 100;
        }
    }

    [GpuKernel]
    public static void ArrayFieldAndMemoryFieldKernel(DiffDeepBag bag, DiffMemoryBag view, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[1] += 10;
            observed[0] = view.Data.Span[0];
            view.Data.Span[1] += 100;
            observed[1] = bag.Data[2];
        }
    }

    [GpuKernel]
    public static void MemoryFieldAndMemoryArgumentKernel(
        DiffMemoryBag bag,
        Memory<int> whole,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data.Span[0] += 10;
            observed[0] = whole.Span[1];
            whole.Span[2] += 100;
            observed[1] = bag.Data.Span[1];
        }
    }

    [GpuKernel]
    public static void MemoryFieldAndWholeArrayKernel(DiffMemoryBag bag, int[] whole, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data.Span[0] += 10;
            observed[0] = whole[1];
            whole[2] += 100;
            observed[1] = bag.Data.Span[1];
        }
    }

    [GpuKernel]
    public static void ElementMemoryFieldAndSliceKernel(
        DiffMemoryBag[] bags,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bags[0].Data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bags[0].Data.Span[2];
        }
    }

    [GpuKernel]
    public static void InElementMemoryFieldAndInSliceKernel(
        [In] DiffMemoryBag[] bags,
        [In] Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bags[0].Data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = bags[0].Data.Span[2];
        }
    }

    [GpuKernel]
    public static void NullableMemoryFieldAndSliceKernel(
        DiffNullableMemoryBag bag,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            var data = bag.Data.GetValueOrDefault();
            data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = data.Span[2];
        }
    }

    [GpuKernel]
    public static void NestedMemoryFieldAndSliceKernel(
        DiffNestedMemoryBag outer,
        Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            outer.Inner.Data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = outer.Inner.Data.Span[2];
        }
    }

    [GpuKernel]
    public static void TwoMemoryFieldsSeparateHalvesKernel(DiffTwoMemoryViews h, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            h.A.Span[1] += 10;
            observed[0] = h.A.Span[1];
            h.B.Span[0] += 100;
            observed[1] = h.B.Span[0];
        }
    }

    [GpuKernel]
    public static void ThreeViewsChainedKernel(DiffTwoMemoryViews h, Span<int> bridge, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            h.A.Span[1] += 10;
            observed[0] = bridge[0];
            bridge[3] += 100;
            observed[1] = h.B.Span[0];
            h.B.Span[1] += 1000;
        }
    }

    [GpuKernel]
    public static void InElementsWithIdenticalMemoryKernel([In] DiffMemoryBag[] bags, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bags[0].Data.Span[1] += 10;
            observed[0] = bags[1].Data.Span[1];
            bags[1].Data.Span[2] += 100;
            observed[1] = bags[0].Data.Span[2];
        }
    }

    [GpuKernel]
    public static void InShortMemoryFieldAndInIntSegmentKernel(
        [In] DiffShortMemoryBag[] mems,
        [In] DiffSegmentBag[] segs,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            mems[0].Data.Span[2] += 10;
            observed[0] = segs[0].Data[1];
            segs[0].Data[2] += 100;
            observed[1] = mems[0].Data.Span[4];
        }
    }

    [GpuKernel]
    public static void InShortMemoryFieldAndInIntArrayCandidateKernel(
        [In] DiffShortMemoryBag[] mems,
        [In] int[] whole,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            mems[0].Data.Span[2] += 10;
            observed[0] = whole[1];
            whole[2] += 100;
            observed[1] = mems[0].Data.Span[4];
        }
    }

    [GpuKernel]
    public static void InImmutableFieldAndInShortMemoryFieldKernel(
        [In] DiffShortMemoryBag[] mems,
        [In] DiffImmutableArrayBag[] imms,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            mems[0].Data.Span[2] += 10;
            observed[0] = imms[0].Data[1];
            mems[0].Data.Span[4] += 100;
            observed[1] = imms[0].Data[2];
        }
    }

    [GpuKernel]
    public static void InReadOnlyMemoryOfBagsAndInSliceKernel(
        [In] DiffReadOnlyMemoryOfBagsHolder[] holders,
        [In] Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            holders[0].Bags.Span[0].Data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = holders[0].Bags.Span[0].Data.Span[2];
        }
    }

    [GpuKernel]
    public static void InImmutableArrayOfBagsAndInSliceKernel(
        [In] DiffImmutableArrayOfBagsHolder[] holders,
        [In] Span<int> slice,
        [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            holders[0].Bags[0].Data.Span[1] += 10;
            observed[0] = slice[0];
            slice[1] += 100;
            observed[1] = holders[0].Bags[0].Data.Span[2];
        }
    }

    // --- Converted elements held by a struct field beside a slice or ref of the same element type (#1858 form 0) ---

    [GpuKernel]
    public static void ConvertedMemoryFieldAndSliceKernel(
        DiffPaddedMemoryHolder holder,
        Span<DiffVec2Padded> slice)
    {
        if (ThreadIndex.X == 0)
        {
            holder.Items.Span[1].A += 10f;
            holder.Items.Span[1].V.X += 100f;
            slice[1].A += slice[0].A + 200f;
            slice[2].V.Y += holder.Items.Span[2].A;
        }
    }

    [GpuKernel]
    public static void ConvertedFieldAndRefElementKernel(
        DiffPaddedHolder holder,
        ref DiffVec2Padded element,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            holder.Items[2].A += 10f;
            observed[0] = element.A;
            element.A += 100f;
            element.V.X += 5f;
            observed[1] = holder.Items[2].A;
        }
    }

    [GpuKernel]
    public static void InConvertedFieldAndInSliceKernel(
        [In] DiffPaddedHolder[] holders,
        [In] Span<DiffVec2Padded> slice,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            holders[0].Items[1].A += 10f;
            observed[0] = slice[0].A;
            slice[1].A += 100f;
            observed[1] = holders[0].Items[2].A;
        }
    }

    [GpuKernel]
    public static void InConvertedMemoryFieldAndInSliceKernel(
        [In] DiffPaddedMemoryHolder[] holders,
        [In] Span<DiffVec2Padded> slice,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            holders[0].Items.Span[1].A += 10f;
            observed[0] = slice[0].A;
            slice[1].A += 100f;
            observed[1] = holders[0].Items.Span[2].A;
        }
    }

    [GpuKernel]
    public static void Vector2MemoryFieldAndSkewedSliceKernel(
        DiffVector2MemoryBag bag,
        Span<Vector2> slice,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data.Span[0] = new Vector2(bag.Data.Span[0].X + 1f, bag.Data.Span[0].Y + 2f);
            observed[0] = slice[0].X;
            observed[1] = slice[0].Y;
        }
    }

    [GpuKernel]
    public static void Vector2ArrayFieldAndSkewedSliceKernel(
        DiffVector2ArrayBag bag,
        Span<Vector2> slice,
        [Out] float[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[0] = new Vector2(bag.Data[0].X + 1f, bag.Data[0].Y + 2f);
            observed[0] = slice[0].X;
            observed[1] = slice[0].Y;
        }
    }
}
