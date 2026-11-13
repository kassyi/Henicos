// ReSharper disable All
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for the StackLinq calls that the transpiler does not fuse into one loop: terminals without a
/// fused strategy (<c>ElementAtOrDefault</c>, <c>SingleOrDefault</c>, <c>SumSequential</c>), queries stored in a local
/// before their terminal (also after <c>AsParallel()</c>), and named arguments in another order than the parameters.
/// Each thread reads a 4-element slice of <c>xs</c>.
/// </summary>
public static partial class StackLinqUnfusedDifferentialKernels
{
    public const int SliceLength = 4;

    /// <summary>The columns of <see cref="UnfusedOperatorKernel"/>.</summary>
    public static readonly string[] UnfusedOperatorColumns =
    [
        "TakeLast(2).ElementAtOrDefault(0)",
        "TakeLast(0).ElementAtOrDefault(0)",
        "TakeLast(-1).ElementAtOrDefault(0)",
        "TakeLast(10).ElementAtOrDefault(3)",
        "TakeLast(3).SkipLast(1).ElementAtOrDefault(1)",
        "TakeLast(3).SkipLast(1).ElementAtOrDefault(2)",
        "SkipLast(3).SingleOrDefault",
        "SkipLast(4).SingleOrDefault",
        "SkipLast(-1).ElementAtOrDefault(3)",
        "SkipLast(2).ElementAtOrDefault(1)",
        "SkipLast(2).ElementAtOrDefault(2)",
        "Static TakeLast(1).ElementAt(0)",
        "Local.Sum(x*2)",
        "Local.LastOrDefault(x>0)",
        "Local.LastOrDefault(false)",
        "Local.Last(true)",
        "Local.Last(x<=x0)",
        "Select((double)x).SumSequential",
        "SumSequential((double)x)",
        "LocalParallel.Sum",
        "Aggregate(func, seed) fused",
        "Aggregate(func, seed) local",
        "Aggregate(func, seed) static",
    ];

    [GpuKernel]
    public static void UnfusedOperatorKernel(int[] xs, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int x0 = slice[0];
        int o = t * 23;

        // TakeLast / SkipLast before a terminal that has no fused strategy
        r[o] = slice.AsStackEnumerable().TakeLast(2).ElementAtOrDefault(0);
        r[o + 1] = slice.AsStackEnumerable().TakeLast(0).ElementAtOrDefault(0);
        r[o + 2] = slice.AsStackEnumerable().TakeLast(-1).ElementAtOrDefault(0);
        r[o + 3] = slice.AsStackEnumerable().TakeLast(10).ElementAtOrDefault(3);
        r[o + 4] = slice.AsStackEnumerable().TakeLast(3).SkipLast(1).ElementAtOrDefault(1);
        r[o + 5] = slice.AsStackEnumerable().TakeLast(3).SkipLast(1).ElementAtOrDefault(2);
        r[o + 6] = slice.AsStackEnumerable().SkipLast(3).SingleOrDefault();
        r[o + 7] = slice.AsStackEnumerable().SkipLast(4).SingleOrDefault();
        r[o + 8] = slice.AsStackEnumerable().SkipLast(-1).ElementAtOrDefault(3);
        r[o + 9] = slice.AsStackEnumerable().SkipLast(2).ElementAtOrDefault(1);
        r[o + 10] = slice.AsStackEnumerable().SkipLast(2).ElementAtOrDefault(2);
        r[o + 11] = StackLinqExtensions.TakeLast(slice.AsStackEnumerable(), 1).ElementAt(0);

        // A query stored in a local is not fused: each terminal is a call of the device StackLinq header
        var local = slice.AsStackEnumerable();
        r[o + 12] = local.Sum(x => x * 2);
        r[o + 13] = local.LastOrDefault(x => x > 0);
        r[o + 14] = local.LastOrDefault(x => x > 0 && x < 0);
        r[o + 15] = local.Last(x => x >= int.MinValue);
        r[o + 16] = local.Last(x => x <= x0);

        r[o + 17] = (long)slice.AsStackEnumerable().Select(x => (double)x).SumSequential();
        r[o + 18] = (long)slice.AsStackEnumerable().SumSequential(x => (double)x);

        // AsParallel() stored in a local: every thread still receives the sum of its whole slice
        var parallel = slice.AsStackEnumerable().AsParallel();
        r[o + 19] = parallel.Sum();

        // Named arguments in the reverse order of the parameters: seed is still the seed
        r[o + 20] = slice.AsStackEnumerable().Aggregate(func: (a, b) => a + b, seed: 10L);
        r[o + 21] = local.Aggregate(func: (a, b) => a - b, seed: 7L);
        r[o + 22] = StackLinqExtensions.Aggregate(func: (a, b) => a * 3 + b, seed: 5L, source: slice.AsStackEnumerable());
    }

    /// <summary>
    /// A floating-point <c>Sum</c> of a query stored in a local: the generated code sums it in order, which the C#
    /// execution does not reproduce, so it has a kernel of its own with a reference model.
    /// </summary>
    [GpuKernel]
    public static void UnfusedDoubleSumKernel(int[] xs, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var local = xs.AsSpan().Slice(baseIdx, SliceLength).AsStackEnumerable();
        r[t] = (long)local.Sum(x => (double)x);
    }

    /// <summary>The columns of <see cref="IndexedLambdaKernel"/>.</summary>
    public static readonly string[] IndexedLambdaColumns =
    [
        "Where((x,i)=>i>0).Sum",
        "Select((x,i)=>x+i).Sum",
        "Count((x,i)=>i!=1)",
        "TakeWhile((x,i)=>i<2).Sum",
        "SkipWhile((x,i)=>i<2).Sum",
        "Where(x>0).Select((x,i)=>i).Sum",
        "Where((x,i)=>i%2==0).AsParallel().Sum",
        "SkipWhile((x,i)=>x<0&&i<3).ElementAtOrDefault(0)",
    ];

    /// <summary>The overloads whose lambda also takes the index of the element; a fused loop has no index to pass.</summary>
    [GpuKernel]
    public static void IndexedLambdaKernel(int[] xs, long[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int baseIdx = t * SliceLength;
        if (baseIdx + SliceLength > xs.Length)
        {
            return;
        }

        var slice = xs.AsSpan().Slice(baseIdx, SliceLength);
        int o = t * 8;

        r[o] = slice.AsStackEnumerable().Where((x, i) => i > 0).Sum();
        r[o + 1] = slice.AsStackEnumerable().Select((x, i) => x + i).Sum();
        r[o + 2] = slice.AsStackEnumerable().Count((x, i) => i != 1);
        r[o + 3] = slice.AsStackEnumerable().TakeWhile((x, i) => i < 2).Sum();
        r[o + 4] = slice.AsStackEnumerable().SkipWhile((x, i) => i < 2).Sum();
        r[o + 5] = slice.AsStackEnumerable().Where(x => x > 0).Select((x, i) => i).Sum();
        r[o + 6] = slice.AsStackEnumerable().Where((x, i) => i % 2 == 0).AsParallel().Sum();
        r[o + 7] = slice.AsStackEnumerable().SkipWhile((x, i) => x < 0 && i < 3).ElementAtOrDefault(0);
    }

    /// <summary>The columns of <see cref="LastPredicateNoMatchKernel"/> and <see cref="UnfusedLastPredicateNoMatchKernel"/>.</summary>
    public static readonly string[] LastPredicateColumns =
    [
        "res",
    ];

    /// <summary>
    /// The fused <c>Last(predicate)</c> after <c>Where</c>: the row whose threshold is above every element has no match.
    /// </summary>
    [GpuKernel]
    public static void LastPredicateNoMatchKernel(int[] xs, int[] thresholds, int[] dst, int rows)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= rows)
        {
            return;
        }

        int threshold = thresholds[t];
        dst[t] = xs.AsSpan().Slice(t * SliceLength, SliceLength).AsStackEnumerable().Where(x => x > 0).Last(x => x > threshold);
    }

    /// <summary>The same <c>Last(predicate)</c> on a query stored in a local, which is not fused.</summary>
    [GpuKernel]
    public static void UnfusedLastPredicateNoMatchKernel(int[] xs, int[] thresholds, int[] dst, int rows)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= rows)
        {
            return;
        }

        int threshold = thresholds[t];
        var query = xs.AsSpan().Slice(t * SliceLength, SliceLength).AsStackEnumerable();
        dst[t] = query.Last(x => x > threshold);
    }
}
