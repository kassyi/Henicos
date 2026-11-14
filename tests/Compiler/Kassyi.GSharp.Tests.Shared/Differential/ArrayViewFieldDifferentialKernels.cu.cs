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
    public static readonly string[] FieldColumns =
    [
        "tag",
        "memory",
        "read_only",
        "segment",
        "immutable",
        "optional",
        "optional_segment",
        "lengths",
    ];

    public static readonly string[] ElementColumns =
    [
        "element_vy",
        "segment_vy",
        "managed",
    ];

    [GpuKernel]
    public static void ReadAndWriteViewsKernel(ArrayViewFields h, int[] r)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i * 8 >= r.Length)
        {
            return;
        }

        int o = i * 8;
        r[o + 0] = h.Tag;
        r[o + 1] = i < h.Memory.Length ? h.Memory.Span[i] : -1;
        r[o + 2] = i < h.ReadOnly.Length ? h.ReadOnly.Span[i] : -1;
        r[o + 3] = i < h.Segment.Count ? h.Segment[i] : -1;
        r[o + 4] = i < h.Immutable.Length ? h.Immutable[i] : -1;
        var optional = h.Optional;
        r[o + 5] = optional.HasValue
            ? (i < optional.GetValueOrDefault().Length ? optional.GetValueOrDefault().Span[i] : -2)
            : -3;
        var optionalSegment = h.OptionalSegment;
        r[o + 6] = optionalSegment.HasValue
            ? (i < optionalSegment.GetValueOrDefault().Count ? optionalSegment.GetValueOrDefault()[i] : -2)
            : -3;
        r[o + 7] = h.Memory.Length + 10 * h.Segment.Count + 100 * h.ReadOnly.Length + 1000 * h.Immutable.Length;

        if (i < h.Memory.Length)
        {
            h.Memory.Span[i] = h.Memory.Span[i] * 2 + i;
        }

        if (i < h.Segment.Count)
        {
            var segment = h.Segment;
            segment[i] = segment[i] + 1000;
        }

        if (optional.HasValue && i < optional.GetValueOrDefault().Length)
        {
            optional.GetValueOrDefault().Span[i] = -i;
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
