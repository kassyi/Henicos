// ReSharper disable All
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential matrix kernels for <c>StackLinq</c> (<c>Kassyi.ZeroAlloc.Linq</c>) fused and unfused device pipelines.
/// Each thread operates on a 4-element slice <c>xs.AsSpan().Slice(t * 4, 4).AsStackEnumerable()</c>.
/// </summary>
public static partial class StackLinqDifferentialKernels
{
    public const int SliceLength = 4;

    /// <summary>The columns of <see cref="Int32StackLinqMatrixKernel"/>.</summary>
    public static readonly string[] Int32StackLinqColumns =
    [
        "Sum",
        "Sum(x=>x)",
        "Where.Sum",
        "Max",
        "Min",
        "UnfusedMax",
        "UnfusedMin",
        "UnfusedSum",
        "Agg(a+b)",
        "Agg(0,a+b)",
        "Agg(10L,a+b)",
        "Count",
        "Count(x>0)",
        "Any",
        "Any(x<0)",
        "All(x>=0)",
        "Contains(x0)",
        "FirstOrDefault",
        "FirstOrDefault(x>0)",
        "FirstOrDefault(false)",
        "LastOrDefault",
        "LastOrDefault(x>0)",
        "LastOrDefault(false)",
        "ElementAtOrDefault(0)",
        "ElementAtOrDefault(3)",
        "ElementAtOrDefault(4)",
        "ElementAtOrDefault(-1)",
        "Skip(0).Sum",
        "Skip(-2).Sum",
        "Skip(2).Sum",
        "Skip(10).Sum",
        "Take(0).Sum",
        "Take(-1).Sum",
        "Take(2).Sum",
        "Take(100).Sum",
        "Where.Skip(-1).Take(2).Sum",
        "Where.Skip(1).Take(-1).Sum",
        "Where(false).Count",
        "Where(false).Any",
        "Where(false).All",
        "Where(false).Sum",
        "SkipWhile(x<0).Sum",
        "TakeWhile(x>=0).Sum",
        "TakeLast(2).Sum",
        "TakeLast(0).Sum",
        "TakeLast(-1).Sum",
        "SkipLast(2).Sum",
        "SkipLast(0).Sum",
        "SkipLast(-1).Sum",
        "SkipLast(10).Sum",
    ];

    [GpuKernel]
    public static void Int32StackLinqMatrixKernel(int[] xs, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int x0 = slice[0];
        int o = t * 50;

        r[o] = slice.AsStackEnumerable().Sum();
        r[o + 1] = slice.AsStackEnumerable().Sum(x => x);
        r[o + 2] = slice.AsStackEnumerable().Where(x => (x & 1) == 0).Sum();
        r[o + 3] = slice.AsStackEnumerable().Max();
        r[o + 4] = slice.AsStackEnumerable().Min();

        var unfused = slice.AsStackEnumerable();
        r[o + 5] = unfused.Max();
        r[o + 6] = unfused.Min();
        r[o + 7] = unfused.Sum();

        r[o + 8] = slice.AsStackEnumerable().Aggregate((a, b) => a + b);
        r[o + 9] = slice.AsStackEnumerable().Aggregate(0, (a, b) => a + b);
        r[o + 10] = slice.AsStackEnumerable().Aggregate(10L, (a, b) => a + b);

        r[o + 11] = slice.AsStackEnumerable().Count();
        r[o + 12] = slice.AsStackEnumerable().Count(x => x > 0);
        r[o + 13] = slice.AsStackEnumerable().Any() ? 1L : 0L;
        r[o + 14] = slice.AsStackEnumerable().Any(x => x < 0) ? 1L : 0L;
        r[o + 15] = slice.AsStackEnumerable().All(x => x >= 0) ? 1L : 0L;
        r[o + 16] = slice.AsStackEnumerable().Contains(x0) ? 1L : 0L;

        r[o + 17] = slice.AsStackEnumerable().FirstOrDefault();
        r[o + 18] = slice.AsStackEnumerable().FirstOrDefault(x => x > 0);
        r[o + 19] = slice.AsStackEnumerable().FirstOrDefault(x => x > 0 && x < 0);
        r[o + 20] = slice.AsStackEnumerable().LastOrDefault();
        r[o + 21] = slice.AsStackEnumerable().LastOrDefault(x => x > 0);
        r[o + 22] = slice.AsStackEnumerable().LastOrDefault(x => x > 0 && x < 0);

        r[o + 23] = slice.AsStackEnumerable().ElementAtOrDefault(0);
        r[o + 24] = slice.AsStackEnumerable().ElementAtOrDefault(3);
        r[o + 25] = slice.AsStackEnumerable().ElementAtOrDefault(4);
        r[o + 26] = slice.AsStackEnumerable().ElementAtOrDefault(-1);

        r[o + 27] = slice.AsStackEnumerable().Skip(0).Sum();
        r[o + 28] = slice.AsStackEnumerable().Skip(-2).Sum();
        r[o + 29] = slice.AsStackEnumerable().Skip(2).Sum();
        r[o + 30] = slice.AsStackEnumerable().Skip(10).Sum();
        r[o + 31] = slice.AsStackEnumerable().Take(0).Sum();
        r[o + 32] = slice.AsStackEnumerable().Take(-1).Sum();
        r[o + 33] = slice.AsStackEnumerable().Take(2).Sum();
        r[o + 34] = slice.AsStackEnumerable().Take(100).Sum();

        r[o + 35] = slice.AsStackEnumerable().Where(x => x >= 0).Skip(-1).Take(2).Sum();
        r[o + 36] = slice.AsStackEnumerable().Where(x => x >= 0).Skip(1).Take(-1).Sum();

        r[o + 37] = slice.AsStackEnumerable().Where(x => x > 0 && x < 0).Count();
        r[o + 38] = slice.AsStackEnumerable().Where(x => x > 0 && x < 0).Any() ? 1L : 0L;
        r[o + 39] = slice.AsStackEnumerable().Where(x => x > 0 && x < 0).All(x => x == 0) ? 1L : 0L;
        r[o + 40] = slice.AsStackEnumerable().Where(x => x > 0 && x < 0).Sum();

        r[o + 41] = slice.AsStackEnumerable().SkipWhile(x => x < 0).Sum();
        r[o + 42] = slice.AsStackEnumerable().TakeWhile(x => x >= 0).Sum();

        r[o + 43] = slice.AsStackEnumerable().TakeLast(2).Sum();
        r[o + 44] = slice.AsStackEnumerable().TakeLast(0).Sum();
        r[o + 45] = slice.AsStackEnumerable().TakeLast(-1).Sum();
        r[o + 46] = slice.AsStackEnumerable().SkipLast(2).Sum();
        r[o + 47] = slice.AsStackEnumerable().SkipLast(0).Sum();
        r[o + 48] = slice.AsStackEnumerable().SkipLast(-1).Sum();
        r[o + 49] = slice.AsStackEnumerable().SkipLast(10).Sum();
    }

    /// <summary>The columns of <see cref="Int32AverageKernel"/>.</summary>
    public static readonly string[] Int32AverageColumns =
    [
        "FusedAverage",
        "Where.Average",
        "UnfusedAverage",
    ];

    [GpuKernel]
    public static void Int32AverageKernel(int[] xs, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int o = t * 3;

        r[o] = slice.AsStackEnumerable().Average();
        // Ensure non-empty for Where.Average by keeping all elements (or testing non-empty subset)
        r[o + 1] = slice.AsStackEnumerable().Where(x => true).Average();
        var unfused = slice.AsStackEnumerable();
        r[o + 2] = unfused.Average();
    }

    /// <summary>The columns of <see cref="SingleFusedAverageKernel"/>.</summary>
    public static readonly string[] SingleFusedAverageColumns =
    [
        "FusedAvgAsFloat",
        "FusedAvgAsDouble",
        "FusedAvgPlus0.1",
        "WhereFusedAvg",
    ];

    [GpuKernel]
    public static void SingleFusedAverageKernel(float[] xs, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int o = t * 4;

        r[o] = slice.AsStackEnumerable().Average();
        r[o + 1] = (double)slice.AsStackEnumerable().Average();
        r[o + 2] = slice.AsStackEnumerable().Average() + 0.1;
        r[o + 3] = slice.AsStackEnumerable().Where(x => true).Average();
    }

    /// <summary>The columns of <see cref="SingleUnfusedAverageKernel"/>.</summary>
    public static readonly string[] SingleUnfusedAverageColumns =
    [
        "UnfusedAvgAsFloat",
        "UnfusedAvgAsDouble",
        "UnfusedAvgPlus0.1",
    ];

    [GpuKernel]
    public static void SingleUnfusedAverageKernel(float[] xs, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        var unfused = slice.AsStackEnumerable();
        int o = t * 3;

        r[o] = unfused.Average();
        r[o + 1] = (double)unfused.Average();
        r[o + 2] = unfused.Average() + 0.1;
    }

    /// <summary>The columns of <see cref="FloatingExtremaAndZeroContainsKernel"/>.</summary>
    public static readonly string[] FloatingExtremaAndZeroContainsColumns =
    [
        "SingleFusedMax",
        "SingleFusedMin",
        "SingleUnfusedMax",
        "SingleUnfusedMin",
        "SingleContainsNegZero",
        "DoubleFusedAverage",
        "DoubleUnfusedAverage",
        "DoubleFusedMax",
        "DoubleFusedMin",
        "DoubleUnfusedMax",
        "DoubleUnfusedMin",
        "DoubleContainsNegZero",
    ];

    [GpuKernel]
    public static void FloatingExtremaAndZeroContainsKernel(float[] fs, double[] ds, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > fs.Length)
        {
            return;
        }

        var fSlice = fs.AsSpan().Slice(baseIdx, SliceLength);
        var dSlice = ds.AsSpan().Slice(baseIdx, SliceLength);
        var fUnfused = fSlice.AsStackEnumerable();
        var dUnfused = dSlice.AsStackEnumerable();
        int o = t * 12;

        r[o] = fSlice.AsStackEnumerable().Max();
        r[o + 1] = fSlice.AsStackEnumerable().Min();
        r[o + 2] = fUnfused.Max();
        r[o + 3] = fUnfused.Min();
        r[o + 4] = fSlice.AsStackEnumerable().Contains(-0.0f) ? 1.0 : 0.0;

        r[o + 5] = dSlice.AsStackEnumerable().Average();
        r[o + 6] = dUnfused.Average();
        r[o + 7] = dSlice.AsStackEnumerable().Max();
        r[o + 8] = dSlice.AsStackEnumerable().Min();
        r[o + 9] = dUnfused.Max();
        r[o + 10] = dUnfused.Min();
        r[o + 11] = dSlice.AsStackEnumerable().Contains(-0.0) ? 1.0 : 0.0;
    }

    /// <summary>The columns of <see cref="ContainsNaNKernel"/>.</summary>
    public static readonly string[] ContainsNaNColumns =
    [
        "FusedContainsFloatNaN",
        "UnfusedContainsFloatNaN",
        "FusedContainsDoubleNaN",
        "UnfusedContainsDoubleNaN",
    ];

    [GpuKernel]
    public static void ContainsNaNKernel(float[] fs, double[] ds, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > fs.Length)
        {
            return;
        }

        var fSlice = fs.AsSpan().Slice(baseIdx, SliceLength);
        var dSlice = ds.AsSpan().Slice(baseIdx, SliceLength);
        var fUnfused = fSlice.AsStackEnumerable();
        var dUnfused = dSlice.AsStackEnumerable();
        int o = t * 4;

        r[o] = fSlice.AsStackEnumerable().Contains(float.NaN) ? 1L : 0L;
        r[o + 1] = fUnfused.Contains(float.NaN) ? 1L : 0L;
        r[o + 2] = dSlice.AsStackEnumerable().Contains(double.NaN) ? 1L : 0L;
        r[o + 3] = dUnfused.Contains(double.NaN) ? 1L : 0L;
    }

    /// <summary>The columns of <see cref="ContainsEvaluationOrderKernel"/>.</summary>
    public static readonly string[] ContainsEvaluationOrderColumns =
    [
        "FusedContainsSideEffectCount",
        "FusedEmptyContainsSideEffectCount",
        "FusedSelectMutatesContainsArg",
    ];

    [GpuKernel]
    public static void ContainsEvaluationOrderKernel(int[] xs, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int x0 = slice[0];
        int o = t * 3;

        int k0 = 0;
        bool c0 = slice.AsStackEnumerable().Contains(++k0);
        r[o] = (c0 ? 100L : 0L) + k0;

        int k1 = 0;
        bool c1 = slice.AsStackEnumerable().Take(0).Contains(++k1);
        r[o + 1] = (c1 ? 100L : 0L) + k1;

        int k2 = x0;
        bool c2 = slice.AsStackEnumerable().Select(x => x + (++k2) * 0).Contains(k2);
        r[o + 2] = c2 ? 1L : 0L;
    }

    /// <summary>The columns of <see cref="UnfusedSelectEvaluationKernel"/>.</summary>
    public static readonly string[] UnfusedSelectEvaluationColumns =
    [
        "SelectSkipElementAtVal",
        "SelectSkipElementAtCount",
        "SelectWhereElementAtVal",
        "SelectWhereElementAtCount",
        "UnfusedSelectMaxCount",
    ];

    [GpuKernel]
    public static void UnfusedSelectEvaluationKernel(int[] xs, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int o = t * 5;

        int k0 = 0;
        int v0 = slice.AsStackEnumerable().Select(x => (x & 0xFFFF) + (++k0)).Skip(2).ElementAtOrDefault(0);
        r[o] = v0;
        r[o + 1] = k0;

        int k1 = 0;
        int v1 = slice.AsStackEnumerable().Select(x => (x & 0xFFFF) + (++k1)).Where(y => true).ElementAtOrDefault(0);
        r[o + 2] = v1;
        r[o + 3] = k1;

        int k2 = 0;
        var unfused = slice.AsStackEnumerable();
        int v2 = unfused.Select(x => (x & 0xFFFF) + (++k2)).Max();
        r[o + 4] = k2;
    }

    /// <summary>The columns of <see cref="NarrowAndInt64StackLinqKernel"/>.</summary>
    public static readonly string[] NarrowAndInt64StackLinqColumns =
    [
        "ByteSelectSum",
        "ShortSelectSum",
        "ByteAggregate",
        "ShortAggregate",
        "Int64Aggregate",
        "Int64Contains",
        "Int64FirstOrDefault",
        "Int64LastOrDefault",
        "Int64ElementAtOrDefault(2)",
    ];

    [GpuKernel]
    public static void NarrowAndInt64StackLinqKernel(byte[] bs, short[] ss, long[] ls, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > ls.Length)
        {
            return;
        }

        var bSlice = bs.AsSpan().Slice(baseIdx, SliceLength);
        var sSlice = ss.AsSpan().Slice(baseIdx, SliceLength);
        var lSlice = ls.AsSpan().Slice(baseIdx, SliceLength);
        int o = t * 9;

        r[o] = bSlice.AsStackEnumerable().Sum(x => (int)x);
        r[o + 1] = sSlice.AsStackEnumerable().Sum(x => (int)x);
        r[o + 2] = bSlice.AsStackEnumerable().Aggregate((byte)0, (a, b) => (byte)(a + b));
        r[o + 3] = sSlice.AsStackEnumerable().Aggregate((short)0, (a, b) => (short)(a + b));
        r[o + 4] = lSlice.AsStackEnumerable().Aggregate(0L, (a, b) => a + b);
        r[o + 5] = lSlice.AsStackEnumerable().Contains(lSlice[1]) ? 1L : 0L;
        r[o + 6] = lSlice.AsStackEnumerable().FirstOrDefault(x => x < 0);
        r[o + 7] = lSlice.AsStackEnumerable().LastOrDefault(x => x > 0);
        r[o + 8] = lSlice.AsStackEnumerable().ElementAtOrDefault(2);
    }

    /// <summary>The columns of <see cref="FloatSequentialSumKernel"/>.</summary>
    public static readonly string[] FloatSequentialSumColumns =
    [
        "FusedSum",
        "WhereSelectSum",
        "UnfusedSum",
    ];

    // Uses Reference model (not Oracle) because host StackLinq.Sum(float) uses Vector<float> SIMD for SpanEnumerator (#730).
    [GpuKernel]
    public static void FloatSequentialSumKernel(float[] xs, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int o = t * 3;

        r[o] = slice.AsStackEnumerable().Sum();
        r[o + 1] = slice.AsStackEnumerable().Where(x => true).Select(x => x).Sum();
        var unfused = slice.AsStackEnumerable();
        r[o + 2] = unfused.Sum();
    }
}
