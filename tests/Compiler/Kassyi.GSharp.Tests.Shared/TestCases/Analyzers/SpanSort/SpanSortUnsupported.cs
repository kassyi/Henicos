using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.SpanSort;

public struct UnorderedValue
{
    public int Value;
}

public struct GenericDescending<T> : IComparer<T>
{
    public int Compare(T a, T b) => 0;
}

[SkipSnapshotConsistency("Analyzer case")]
public static partial class SpanSortUnsupported
{
    [GpuKernel]
    public static void ReferenceComparerKernel(Span<int> data)
    {
        data.Sort(Comparer<int>.Create((a, b) => a - b));
    }

    [GpuKernel]
    public static void NullComparisonKernel(Span<int> data)
    {
        MemoryExtensions.Sort(data, default(Comparison<int>));
    }

    [GpuKernel]
    public static void UnorderedElementKernel(Span<UnorderedValue> values)
    {
        values.Sort();
    }

    [GpuKernel]
    public static void GenericComparerKernel(Span<int> data)
    {
        data.Sort(new GenericDescending<int>());
    }

    [GpuKernel]
    public static void ReorderedArgumentsKernel(Span<int> data, Span<int> items)
    {
        MemoryExtensions.Sort(items: items, keys: data);
    }

    [GpuKernel]
    public static void ComparisonVariableKernel(Span<int> data)
    {
        Comparison<int> byValue = (a, b) => a - b;
        data.Sort(byValue);
    }

    [GpuKernel]
    public static void SeveralComparersKernel(Span<int> data)
    {
        data.Sort(new TwoKeyComparer());
    }

    [GpuKernel]
    public static void NativeIntegerKernel(Span<nint> data)
    {
        data.Sort();
    }
}

public struct TwoKeyComparer : IComparer<int>, IComparer<long>
{
    public int Compare(int a, int b) => 0;

    public int Compare(long a, long b) => 0;
}
