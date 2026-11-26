namespace Kassyi.GSharp.Tests.Shared.Syntax;

using System;
using Kassyi.GSharp.Core;

/// <summary>A struct used to test <c>Fill</c>, <c>Clear</c>, <c>Reverse</c>, and <c>CopyTo</c> on non-primitive elements.</summary>
public struct SpanWritePair : IEquatable<SpanWritePair>
{
    public int A;
    public int B;

    public bool Equals(SpanWritePair other) => A == other.A && B == other.B;

    public override int GetHashCode() => HashCode.Combine(A, B);

    public override string ToString() => $"({A}, {B})";
}

public struct SpanSliceReadSource
{
    [GpuReadOnly]
    public int[] Buffer;
    public int Offset;
    public int Count;
}

public struct SpanSliceWriteTarget
{
    public int[] Buffer;
    public int Offset;
    public int Count;
}

public struct SpanSliceCopyRange
{
    public int SourceOffset;
    public int SourceCount;
    public int DestinationOffset;
    public int DestinationCount;
}

public struct ArraySliceSource
{
    [GpuReadOnly]
    public int[] Buffer;
    public int StartIndex;
}

public struct ArraySliceDestination
{
    public int[] Buffer;
    public int StartIndex;
}

public struct EvaluationOrderSpanTargets
{
    public int[] SliceCopyTarget;
    public int[] FillTarget;
    public int[] ArrayCopyTarget;
    public int[] Counters;
}

/// <summary>
/// Kernels exercising <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, and <c>System.Array</c>
/// write / slice members (#731).
/// </summary>
public static partial class SpanWriteKernels
{
    // Issue #731 minimal reproduction: per-thread AsSpan(start, length), Fill, and Reverse
    [GpuKernel]
    public static void Issue731AsSpanFillReverseKernel(int[] data)
    {
        var tid = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        Span<int> mine = data.AsSpan(tid * 4, 4);
        mine.Fill(7);
        mine[0] = 1;
        mine.Reverse();
    }

    [GpuKernel]
    public static void FillSliceKernel(int[] data, int offset, int count, int value)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> span = data;
        span.Slice(offset, count).Fill(value);
    }

    [GpuKernel]
    public static void ClearSliceKernel(int[] data, int offset, int count)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> span = data;
        span.Slice(offset, count).Clear();
    }

    [GpuKernel]
    public static void ClearStructSliceKernel(SpanWritePair[] data, int offset, int count)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        data.AsSpan(offset, count).Clear();
    }

    [GpuKernel]
    public static void StackAllocFillClearReverseKernel(int[] output)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> scratch = stackalloc int[8];
        scratch.Fill(9);
        scratch.Slice(2, 3).Clear();
        scratch[0] = 42;
        scratch.Reverse();
        scratch.CopyTo(output.AsSpan(0, 8));
    }

    [GpuKernel]
    public static void ReverseSliceKernel(int[] data, int offset, int count)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> span = data;
        span.Slice(offset, count).Reverse();
    }

    [GpuKernel]
    public static void ReverseVariantsKernel(int[] a, int[] b, int[] c)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        MemoryExtensions.Reverse(a.AsSpan());
        b.Reverse();

        int[] local = [10, 20, 30, 40, 50];
        local.Reverse();
        local.CopyTo(c.AsSpan(0, 5));
    }

    [GpuKernel]
    public static void CopyToSameBufferKernel(int[] data, int srcOffset, int dstOffset, int count)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> span = data;
        span.Slice(srcOffset, count).CopyTo(span.Slice(dstOffset, count));
    }

    [GpuKernel]
    public static void CopyToStructSameBufferKernel(SpanWritePair[] data, int srcOffset, int dstOffset, int count)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<SpanWritePair> span = data;
        span.Slice(srcOffset, count).CopyTo(span.Slice(dstOffset, count));
    }

    [GpuKernel]
    public static void CopyToVariantsKernel(int[] src, int[] dst1, int[] dst2, int[] dst3)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        ReadOnlySpan<int> ro = src;
        ro.Slice(1, 4).CopyTo(dst1.AsSpan(2, 5));

        src.CopyTo(dst2.AsSpan(1));

        int[] local = [100, 200, 300];
        local.CopyTo(dst3.AsSpan(0, 4));
    }

    [GpuKernel]
    public static void CopyToDirectKernel(int[] src, int[] dst)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        src.AsSpan().CopyTo(dst.AsSpan());
    }

    [GpuKernel]
    public static void TryCopyToSliceKernel(SpanSliceReadSource source, SpanSliceWriteTarget destination, int[] copied)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        int[] src = source.Buffer;
        int srcOffset = source.Offset;
        int srcCount = source.Count;
        int[] dst = destination.Buffer;
        int dstOffset = destination.Offset;
        int dstCount = destination.Count;
        int[] ok = copied;

        ReadOnlySpan<int> s = src.AsSpan(srcOffset, srcCount);
        Span<int> d = dst.AsSpan(dstOffset, dstCount);
        ok[0] = s.TryCopyTo(d) ? 1 : 0;
    }

    [GpuKernel]
    public static void TryCopyToSameBufferKernel(int[] data, SpanSliceCopyRange range, int[] copied)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        int srcOffset = range.SourceOffset;
        int srcCount = range.SourceCount;
        int dstOffset = range.DestinationOffset;
        int dstCount = range.DestinationCount;
        int[] ok = copied;

        Span<int> span = data;
        ok[0] = span.Slice(srcOffset, srcCount).TryCopyTo(span.Slice(dstOffset, dstCount)) ? 1 : 0;
    }

    [GpuKernel]
    public static void AsSpanOverloadsKernel(int[] data, int start, int length, int[] results)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> full = data.AsSpan();
        Span<int> tail = data.AsSpan(start);
        Span<int> sub = data.AsSpan(start, length);
        Span<int> atEnd1 = data.AsSpan(data.Length);
        Span<int> atEnd2 = data.AsSpan(data.Length, 0);

        results[0] = full.Length;
        results[1] = tail.Length;
        results[2] = sub.Length;
        results[3] = atEnd1.Length + atEnd2.Length;
        if (!sub.IsEmpty)
        {
            sub[0] += 100;
        }
    }

    [GpuKernel]
    public static void SpanEmptyAndIsEmptyKernel(int[] data, int[] results)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Span<int> emptySpan = Span<int>.Empty;
        ReadOnlySpan<int> emptyRo = ReadOnlySpan<int>.Empty;
        Span<int> nonEmpty = data.AsSpan();
        ReadOnlySpan<int> nonEmptyRo = data;
        Span<int> zeroTail = nonEmpty.Slice(nonEmpty.Length);

        emptySpan.Fill(123);
        emptySpan.Clear();
        emptySpan.Reverse();
        emptySpan.CopyTo(nonEmpty);
        var copiedEmpty = emptyRo.TryCopyTo(emptySpan);

        results[0] = emptySpan.IsEmpty && emptySpan.Length == 0 ? 1 : 0;
        results[1] = emptyRo.IsEmpty && emptyRo.Length == 0 ? 1 : 0;
        results[2] = !nonEmpty.IsEmpty && !nonEmptyRo.IsEmpty ? 1 : 0;
        results[3] = zeroTail.IsEmpty && copiedEmpty ? 1 : 0;
    }

    [GpuKernel]
    public static void ArrayFillAndClearKernel(int[] a, int[] b, int[] c, int[] d)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Fill(a, 5);
        Array.Fill(b, 8, 2, 3);
        Array.Clear(c);
        Array.Clear(d, 1, 4);
    }

    [GpuKernel]
    public static void ArrayReverseKernel(int[] a, int[] b)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Reverse(a);
        Array.Reverse(b, 2, 5);
    }

    [GpuKernel]
    public static void ArrayCopy3Kernel(int[] src, int[] dst, int length)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Copy(src, dst, length);
    }

    [GpuKernel]
    public static void ArrayCopy5Kernel(ArraySliceSource source, ArraySliceDestination destination, int length)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        int[] src = source.Buffer;
        int srcIdx = source.StartIndex;
        int[] dst = destination.Buffer;
        int dstIdx = destination.StartIndex;

        Array.Copy(src, srcIdx, dst, dstIdx, length);
    }

    [GpuKernel]
    public static void ArrayCopySameBufferKernel(int[] data, int srcIdx, int dstIdx, int length)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Copy(data, srcIdx, data, dstIdx, length);
    }

    [GpuKernel]
    public static void ArraySortKeysKernel(int[] keys)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Sort(keys);
    }

    [GpuKernel]
    public static void ArraySortFloatKeysKernel(float[] keys)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Sort(keys);
    }

    [GpuKernel]
    public static void ArraySortPairsKernel(int[] keys, int[] items)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Sort(keys, items);
    }

    [GpuKernel]
    public static void EvaluationOrderSpanWriteKernel(int[] src, EvaluationOrderSpanTargets targets)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        int[] dst1 = targets.SliceCopyTarget;
        int[] dst2 = targets.FillTarget;
        int[] dst3 = targets.ArrayCopyTarget;
        int[] counters = targets.Counters;

        int i = 1;
        Span<int> sub = src.AsSpan().Slice(i++, i++);
        sub.CopyTo(dst1.AsSpan());
        counters[0] = i;

        int j = 1;
        Array.Fill(dst2, j * 10, j++, ++j);
        counters[1] = j;

        int k = 0;
        int len = 2;
        Array.Copy(src, k++, dst3, k++, ++len);
        counters[2] = k;
        counters[3] = len;
    }

    [GpuKernel]
    public static void OutOfRangeArrayClearKernel(int[] a)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Clear(a, 2, 10);
    }

    [GpuKernel]
    public static void OutOfRangeArrayFillKernel(int[] a)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Fill(a, 5, 2, 10);
    }

    [GpuKernel]
    public static void OutOfRangeArrayCopyKernel(int[] src, int[] dst)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Copy(src, 0, dst, 2, 4);
    }

    [GpuKernel]
    public static void OutOfRangeAsSpanFillKernel(int[] a)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        a.AsSpan(2, 10).Fill(5);
    }

    [GpuKernel]
    public static void OutOfRangeSliceFillKernel(int[] a)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        a.AsSpan().Slice(2, 10).Fill(5);
    }

    // A System.Array call on a null array: ArgumentNullException before the call reads anything. dst[0] is written
    // before the call, dst[1] after it; length 0 keeps every range check from failing first
    [GpuKernel]
    public static void ArrayNullArgumentKernel(int[] a, int[] b, int mode, int[] dst)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        dst[0] = 1;
        if (mode == 0)
        {
            Array.Fill(a, 5);
        }
        else if (mode == 1)
        {
            Array.Fill(a, 5, 0, 0);
        }
        else if (mode == 2)
        {
            Array.Clear(a);
        }
        else if (mode == 3)
        {
            Array.Clear(a, 0, 0);
        }
        else if (mode == 4)
        {
            Array.Reverse(a);
        }
        else if (mode == 5)
        {
            Array.Reverse(a, 0, 0);
        }
        else if (mode == 6)
        {
            Array.Copy(a, b, 0);
        }
        else
        {
            Array.Copy(a, 0, b, 0, 0);
        }

        dst[1] = 100;
    }

    // One thread reverses a range that the test chooses, to check the exception of a range past the end
    [GpuKernel]
    public static void ArrayReverseRangeKernel(int[] a, int index, int length)
    {
        if (ThreadIndex.X != 0 || BlockIndex.X != 0)
        {
            return;
        }

        Array.Reverse(a, index, length);
    }
}
