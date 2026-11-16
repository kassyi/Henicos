#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>Struct fields of array-like types other than arrays, and their nullable forms (#1267, #1115).</summary>
public struct ArrayViewFields
{
    public int Tag;
    public Memory<int> Memory;
    public ReadOnlyMemory<int> ReadOnly;
    public ArraySegment<int> Segment;
    public ImmutableArray<int> Immutable;
    public Memory<int>? Optional;
    public ArraySegment<int>? OptionalSegment;
}

/// <summary>A struct with views, nested in <see cref="OuterViewFields"/> and used as an array element.</summary>
public struct NestedViewFields
{
    public int Tag;
    public Memory<int> Memory;
    public ArraySegment<int> Segment;
}

/// <summary>A by-value parameter that holds <see cref="NestedViewFields"/>.</summary>
public struct OuterViewFields
{
    public int Tag;
    public NestedViewFields Inner;
}

/// <summary>An element whose CUDA layout differs from the host's (<c>V</c> at 4 on the host, at 8 on CUDA).</summary>
public struct ArrayViewElement
{
    public float X;
    public Vector2 V;
}

/// <summary>Views of converted elements and a view over memory that no array holds.</summary>
public struct ArrayViewElementFields
{
    public Memory<ArrayViewElement> Elements;
    public ArraySegment<ArrayViewElement> ElementSegment;
    public Memory<int> Managed;
}

/// <summary>
/// Differential kernels for struct fields of type <c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>,
/// <c>ArraySegment&lt;T&gt;</c>, <c>ImmutableArray&lt;T&gt;</c> and their nullable forms: the kernel sees the
/// elements of the view (offset and length) and its writes reach the memory the view covers.
/// </summary>
public static partial class ArrayViewFieldDifferentialKernels
{
    public static readonly string[] ElementColumns =
    [
        "element_vy",
        "segment_vy",
        "managed",
    ];

    // Each kernel reads (and, where the view is writable, writes) one container: row i is thread i, -1 is past the end

    [GpuKernel]
    public static void MemoryKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= r.Length)
        {
            return;
        }

        r[i] = i < h.Memory.Length ? h.Memory.Span[i] : -1;
        if (i < h.Memory.Length)
        {
            h.Memory.Span[i] = h.Memory.Span[i] * 2 + i;
        }
    }

    [GpuKernel]
    public static void ReadOnlyMemoryKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= r.Length)
        {
            return;
        }

        r[i] = i < h.ReadOnly.Length ? h.ReadOnly.Span[i] : -1;
    }

    [GpuKernel]
    public static void SegmentKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= r.Length)
        {
            return;
        }

        r[i] = i < h.Segment.Count ? h.Segment[i] : -1;
        if (i < h.Segment.Count)
        {
            var segment = h.Segment;
            segment[i] = segment[i] + 1000;
        }
    }

    [GpuKernel]
    public static void ImmutableArrayKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= r.Length)
        {
            return;
        }

        r[i] = i < h.Immutable.Length ? h.Immutable[i] : -1;
    }

    [GpuKernel]
    public static void OptionalMemoryKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= r.Length)
        {
            return;
        }

        var optional = h.Optional;
        r[i] = optional.HasValue
            ? (i < optional.GetValueOrDefault().Length ? optional.GetValueOrDefault().Span[i] : -2)
            : -3;
        if (optional.HasValue && i < optional.GetValueOrDefault().Length)
        {
            optional.GetValueOrDefault().Span[i] = -i;
        }
    }

    [GpuKernel]
    public static void OptionalSegmentKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= r.Length)
        {
            return;
        }

        var optionalSegment = h.OptionalSegment;
        r[i] = optionalSegment.HasValue
            ? (i < optionalSegment.GetValueOrDefault().Count ? optionalSegment.GetValueOrDefault()[i] : -2)
            : -3;
    }

    // The tag and the lengths of every view, with no element read
    [GpuKernel]
    public static void LengthsKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i * 2 >= r.Length)
        {
            return;
        }

        r[i * 2] = h.Tag;
        r[i * 2 + 1] = h.Memory.Length + 10 * h.Segment.Count + 100 * h.ReadOnly.Length + 1000 * h.Immutable.Length;
    }

    // Views inside a struct nested in a by-value parameter
    [GpuKernel]
    public static void NestedByValueKernel(OuterViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i * 2 >= r.Length)
        {
            return;
        }

        r[i * 2] = i < h.Inner.Memory.Length ? h.Inner.Memory.Span[i] : -1;
        r[i * 2 + 1] = i < h.Inner.Segment.Count ? h.Inner.Segment[i] : -1;
        if (i < h.Inner.Memory.Length)
        {
            h.Inner.Memory.Span[i] = h.Inner.Memory.Span[i] + h.Tag;
        }

        if (i < h.Inner.Segment.Count)
        {
            var segment = h.Inner.Segment;
            segment[i] = segment[i] + 1000;
        }
    }

    // Views inside the elements of an array of structs, each over its own array
    [GpuKernel]
    public static void ArrayOfStructsKernel(NestedViewFields[] items, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i * 4 >= r.Length)
        {
            return;
        }

        r[i * 4] = i < items[0].Memory.Length ? items[0].Memory.Span[i] : -1;
        r[i * 4 + 1] = i < items[0].Segment.Count ? items[0].Segment[i] : -1;
        r[i * 4 + 2] = i < items[1].Memory.Length ? items[1].Memory.Span[i] : -1;
        r[i * 4 + 3] = i < items[1].Segment.Count ? items[1].Segment[i] : -1;
        if (i < items[1].Memory.Length)
        {
            items[1].Memory.Span[i] = items[1].Memory.Span[i] + items[1].Tag;
        }
    }

    [GpuKernel]
    public static void ConvertedElementsKernel(ArrayViewElementFields h, float[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i * 3 >= r.Length)
        {
            return;
        }

        int o = i * 3;
        r[o + 0] = i < h.Elements.Length ? h.Elements.Span[i].V.Y : -1f;
        r[o + 1] = i < h.ElementSegment.Count ? h.ElementSegment[i].V.Y : -1f;
        r[o + 2] = i < h.Managed.Length ? h.Managed.Span[i] : -1f;
        if (i < h.Elements.Length)
        {
            h.Elements.Span[i].V = new Vector2(i, h.Elements.Span[i].X);
        }

        if (i < h.Managed.Length)
        {
            h.Managed.Span[i] = h.Managed.Span[i] + 7;
        }
    }
}
