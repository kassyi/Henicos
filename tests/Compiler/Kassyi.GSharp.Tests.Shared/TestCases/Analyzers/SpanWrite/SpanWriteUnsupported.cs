using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.SpanWrite;

public struct SpanWriteDescComparer : IComparer<int>
{
    public int Compare(int x, int y) => y.CompareTo(x);
}

[SkipSnapshotConsistency("Analyzer case")]
public static partial class SpanWriteUnsupportedKernels
{
    [GpuKernel]
    public static void IndexOfKernel(int[] data, int[] result)
    {
        Span<int> span = data;
        result[0] = span.IndexOf(1);
    }

    [GpuKernel]
    public static void ReadOnlyContainsKernel(int[] data, int[] result)
    {
        ReadOnlySpan<int> span = data;
        result[0] = span.Contains(1) ? 1 : 0;
    }

    [GpuKernel]
    public static void SequenceEqualKernel(int[] a, int[] b, int[] result)
    {
        Span<int> sa = a;
        ReadOnlySpan<int> sb = b;
        result[0] = sa.SequenceEqual(sb) ? 1 : 0;
    }

    [GpuKernel]
    public static void ReplaceKernel(int[] data)
    {
        Span<int> span = data;
        span.Replace(1, 2);
    }

    [GpuKernel]
    public static void ToArrayKernel(int[] data, int[] result)
    {
        Span<int> span = data;
        var copy = span.ToArray();
        result[0] = copy.Length;
    }

    [GpuKernel]
    public static void GetPinnableReferenceKernel(int[] data, int[] result)
    {
        Span<int> span = data;
        result[0] = span.GetPinnableReference();
    }

    [GpuKernel]
    public static void AsSpanIndexKernel(int[] data, int[] result)
    {
        var span = data.AsSpan(Index.FromStart(1));
        result[0] = span.Length;
    }

    [GpuKernel]
    public static void AsSpanRangeKernel(int[] data, int[] result)
    {
        var span = data.AsSpan(1..3);
        result[0] = span.Length;
    }

    [GpuKernel]
    public static void ArrayResizeKernel(int[] data)
    {
        Array.Resize(ref data, 8);
    }

    [GpuKernel]
    public static void ArrayIndexOfKernel(int[] data, int[] result)
    {
        result[0] = Array.IndexOf(data, 1);
    }

    [GpuKernel]
    public static void ArrayEmptyKernel(int[] result)
    {
        Span<int> empty = Array.Empty<int>();
        result[0] = empty.Length;
    }

    [GpuKernel]
    public static void ArraySortRangeKernel(int[] data)
    {
        Array.Sort(data, 0, 2);
    }

    [GpuKernel]
    public static void ArraySortNonGenericKernel(int[] data)
    {
        Array.Sort((Array)data);
    }

    [GpuKernel]
    public static void ArraySortComparerKernel(int[] data)
    {
        Array.Sort(data, new SpanWriteDescComparer());
    }

    [GpuKernel]
    public static void ArrayCopyLongKernel(int[] src, int[] dst)
    {
        Array.Copy(src, dst, 2L);
    }

    [GpuKernel]
    public static void ArrayInstanceCopyToKernel(int[] src, int[] dst)
    {
        src.CopyTo(dst, 0);
    }

    [GpuKernel]
    public static void ArrayRankPropertyKernel(int[] data, int[] result)
    {
        result[0] = data.Rank;
    }

    [GpuKernel]
    public static void MethodGroupArrayReverseKernel(int[] data)
    {
        Action<int[]> action = Array.Reverse;
        action(data);
    }

    [GpuKernel]
    public static void MethodGroupMemoryExtensionsReverseKernel(int[] data)
    {
        Action<int[]> action = MemoryExtensions.Reverse;
        action(data);
    }

    [GpuKernel]
    public static void SupportedAllMembersKernel(int[] data, int[] other, int[] items)
    {
        Span<int> s = data.AsSpan();
        Span<int> s1 = data.AsSpan(1);
        Span<int> s2 = data.AsSpan(1, 2);
        ReadOnlySpan<int> ro = other;
        Span<int> empty = Span<int>.Empty;
        ReadOnlySpan<int> roEmpty = ReadOnlySpan<int>.Empty;

        if (empty.IsEmpty && roEmpty.IsEmpty && !s.IsEmpty && !ro.IsEmpty)
        {
            s1.Fill(3);
            s2.Clear();
            s2.Reverse();
            MemoryExtensions.Reverse(s1);
            data.Reverse();
            ro.Slice(0, 2).CopyTo(s.Slice(0, 2));
            _ = ro.Slice(0, 2).TryCopyTo(s.Slice(0, 2));
            other.CopyTo(s);
            Array.Fill(data, 1);
            Array.Fill(data, 2, 0, 2);
            Array.Clear(data);
            Array.Clear(data, 0, 2);
            Array.Reverse(data);
            Array.Reverse(data, 0, 2);
            Array.Copy(other, data, 2);
            Array.Copy(other, 0, data, 0, 2);
            Array.Sort(data);
            Array.Sort(data, items);
        }
    }
}
