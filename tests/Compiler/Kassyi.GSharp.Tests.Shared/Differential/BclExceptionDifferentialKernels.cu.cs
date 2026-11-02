#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Two-field value type used to verify <c>Nullable&lt;T&gt;.Value</c> and <c>GetValueOrDefault()</c> on user-defined
/// structs in <see cref="BclExceptionDifferentialKernels"/>.
/// </summary>
public struct DiffNullablePairStruct
{
    public int X;
    public int Y;
}

/// <summary>
/// Differential kernels for Lane D5 Stage 3: <see cref="Nullable{T}.Value"/> (<see cref="InvalidOperationException"/>,
/// #718), <c>Math.Clamp</c> (<see cref="ArgumentException"/> when <c>min &gt; max</c>, #718), <c>StackLinq</c> empty and
/// multi-element sequence traps (<see cref="InvalidOperationException"/> and <see cref="ArgumentOutOfRangeException"/>),
/// <c>ElementAtOrDefault(-1)</c> non-Span enumeration side effects (S31), <c>MemoryExtensions.Sort</c> length mismatch
/// and comparer exception wrapping, and post-trap non-continuation before <c>100 / denom</c> (S30 / #729).
/// </summary>
public static partial class BclExceptionDifferentialKernels
{
    public const int SliceWidth = 4;

    public static readonly string[] NullableValueColumns =
    [
        "int_val",
        "int_default",
        "int_coalesce",
        "pair_sum",
        "pair_default_x",
    ];

    public static readonly string[] MathClampIntColumns =
    [
        "clamp_int",
    ];

    public static readonly string[] MathClampFloatColumns =
    [
        "clamp_float",
    ];

    public static readonly string[] MathClampDoubleColumns =
    [
        "clamp_double",
    ];

    public static readonly string[] StackLinqTrapColumns =
    [
        "fused_first",
        "unfused_first",
        "fused_last",
        "single_one",
        "single_or_default_empty",
        "fused_min",
        "fused_max",
        "fused_avg_int",
        "fused_agg",
    ];

    public static readonly string[] StackLinqElementAtColumns =
    [
        "span_elem_at",
        "select_elem_at",
        "span_neg_default",
        "span_oob_default",
        "select_neg_default",
    ];

    public static readonly string[] SortExceptionColumns =
    [
        "k0",
        "k3",
        "v0",
        "v3",
    ];

    /// <summary>
    /// L-01: <see cref="Nullable{T}.Value"/> on <c>int?</c> and <c>DiffNullablePairStruct?</c> when <c>HasValue</c> is
    /// <see langword="true"/> (Seed 0) vs <see langword="false"/> (Seeds 1..2, throwing
    /// <see cref="InvalidOperationException"/> on C# paths, #718).
    /// </summary>
    [GpuKernel]
    public static void NullableValueAccessKernel(
        int[] hasInt,
        int[] intVals,
        int[] hasPair,
        int[] pairX,
        int[] pairY,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int? optInt = default;
        if (hasInt[t] != 0)
        {
            optInt = intVals[t];
        }

        int? emptyInt = default;
        int defInt = emptyInt ?? -999;
        int coalescedInt = optInt ?? -777;

        DiffNullablePairStruct? optPair = default;
        if (hasPair[t] != 0)
        {
            optPair = new DiffNullablePairStruct { X = pairX[t], Y = pairY[t] };
        }

        DiffNullablePairStruct? emptyPair = default;
        DiffNullablePairStruct coalescedPair = emptyPair ?? new DiffNullablePairStruct { X = -555, Y = -444 };
        int defPairX = coalescedPair.X;

#pragma warning disable CS8629 // Deliberate Nullable<T>.Value access on potentially null value to test InvalidOperationException (#718).
        int unwrappedInt = optInt.Value;
        DiffNullablePairStruct unwrappedPair = optPair.Value;
#pragma warning restore CS8629

        int baseIdx = t * 5;
        dst[baseIdx + 0] = unwrappedInt;
        dst[baseIdx + 1] = defInt;
        dst[baseIdx + 2] = coalescedInt;
        dst[baseIdx + 3] = unwrappedPair.X + unwrappedPair.Y;
        dst[baseIdx + 4] = defPairX;
    }

    /// <summary>
    /// L-02: <see cref="Math.Clamp(int, int, int)"/>, <see cref="Math.Clamp(float, float, float)"/>, and
    /// <see cref="Math.Clamp(double, double, double)"/> with valid/NaN bounds (Seed 0) and <c>min &gt; max</c>
    /// (Seeds 1..3, throwing <see cref="ArgumentException"/> on C# paths, #718).
    /// </summary>
    [GpuKernel]
    public static void MathClampBoundsAndNaNKernel(
        int[] iVal,
        int[] iMin,
        int[] iMax,
        float[] fVal,
        float[] fMin,
        float[] fMax,
        double[] dVal,
        double[] dMin,
        double[] dMax,
        int[] iDst,
        float[] fDst,
        double[] dDst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        iDst[t] = Math.Clamp(iVal[t], iMin[t], iMax[t]);
        fDst[t] = Math.Clamp(fVal[t], fMin[t], fMax[t]);
        dDst[t] = Math.Clamp(dVal[t], dMin[t], dMax[t]);
    }

    /// <summary>
    /// L-03: <c>StackLinq</c> terminal operators (<c>First</c>, <c>Last</c>, <c>Single</c>, <c>SingleOrDefault</c>,
    /// <c>Min</c>, <c>Max</c>, <c>Average</c>, <c>Aggregate</c>) on valid slices (Seed 0) vs empty or multi-element
    /// slices (Seeds 1..10, throwing <see cref="InvalidOperationException"/> on both Managed and Native).
    /// </summary>
    [GpuKernel]
    public static void StackLinqEmptyAndMultiElementTrapKernel(
        int[] backing,
        int[] lengths,
        int[] modes,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int sliceOffset = t * SliceWidth;
        int len = lengths[t];
        int mode = modes[t];
        Span<int> slice = backing.AsSpan().Slice(sliceOffset, len);
        Span<int> singleSlice = backing.AsSpan().Slice(sliceOffset, 1);
        Span<int> emptySlice = backing.AsSpan().Slice(sliceOffset, 0);

        int baseIdx = t * 9;
        if (mode == 0)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().First();
            dst[baseIdx + 1] = slice.AsStackEnumerable().Select(x => x + 1).First();
            dst[baseIdx + 2] = slice.AsStackEnumerable().Last();
            dst[baseIdx + 3] = singleSlice.AsStackEnumerable().Single();
            dst[baseIdx + 4] = emptySlice.AsStackEnumerable().SingleOrDefault();
            dst[baseIdx + 5] = slice.AsStackEnumerable().Min();
            dst[baseIdx + 6] = slice.AsStackEnumerable().Max();
            dst[baseIdx + 7] = (int)slice.AsStackEnumerable().Average();
            dst[baseIdx + 8] = slice.AsStackEnumerable().Aggregate((a, b) => a + b);
        }
        else if (mode == 1)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().First();
        }
        else if (mode == 2)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().First(x => x > 1000000);
        }
        else if (mode == 3)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().Last();
        }
        else if (mode == 4)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().Single();
        }
        else if (mode == 5)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().Single(x => x > -1000000);
        }
        else if (mode == 6)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().SingleOrDefault();
        }
        else if (mode == 7)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().Min();
        }
        else if (mode == 8)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().Max();
        }
        else if (mode == 9)
        {
            dst[baseIdx + 0] = (int)slice.AsStackEnumerable().Average();
        }
        else
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().Aggregate((a, b) => a + b);
        }
    }

    /// <summary>
    /// L-04: <c>StackLinq.ElementAt</c> and <c>ElementAtOrDefault</c> across Span-backed and <c>Select</c>-backed
    /// sequences. Seeds 1..2 throw <see cref="ArgumentOutOfRangeException"/> on both Managed and Native.
    /// </summary>
    [GpuKernel]
    public static void StackLinqElementAtAndNegativeDefaultKernel(
        int[] backing,
        int[] indices,
        int[] modes,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> slice = backing.AsSpan().Slice(t * SliceWidth, SliceWidth);
        int idx = indices[t];
        int mode = modes[t];
        int baseIdx = t * 5;

        if (mode == 0)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().ElementAt(idx);
            dst[baseIdx + 1] = slice.AsStackEnumerable().Select(x => x * 2).ElementAt(idx);
            dst[baseIdx + 2] = slice.AsStackEnumerable().ElementAtOrDefault(-1);
            dst[baseIdx + 3] = slice.AsStackEnumerable().ElementAtOrDefault(SliceWidth);
            dst[baseIdx + 4] = slice.AsStackEnumerable().Select(x => x * 2).ElementAtOrDefault(-1);
        }
        else if (mode == 1)
        {
            dst[baseIdx + 0] = slice.AsStackEnumerable().ElementAt(idx);
        }
        else
        {
            dst[baseIdx + 1] = slice.AsStackEnumerable().Select(x => x * 2).ElementAt(idx);
        }
    }

    /// <summary>
    /// S31 witness: <c>ElementAtOrDefault(-1)</c> on non-Span <c>Select</c> and <c>Where</c> sequences.
    /// <c>Select</c> evaluates its selector 4 times on C# Managed (eager in <c>MoveNext</c>) vs 0 times on Native/CUDA
    /// (lazy in <c>Current</c>), and <c>Where</c> evaluates its predicate 4 times on all paths instead of returning
    /// <c>default</c> immediately when <c>index &lt; 0</c>.
    /// </summary>
    [GpuKernel]
    public static void ElementAtOrDefaultNegativeIndexSideEffectReproKernel(
        int[] backing,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> slice = backing.AsSpan().Slice(t * SliceWidth, SliceWidth);
        int selectEvaluations = 0;
        int selectVal = slice.AsStackEnumerable()
            .Select(x => x + (++selectEvaluations))
            .ElementAtOrDefault(-1);

        int whereEvaluations = 0;
        int whereVal = slice.AsStackEnumerable()
            .Where(x => (x | (++whereEvaluations)) != 0)
            .ElementAtOrDefault(-1);

        int baseIdx = t * 3;
        dst[baseIdx + 0] = selectVal + whereVal;
        dst[baseIdx + 1] = selectEvaluations;
        dst[baseIdx + 2] = whereEvaluations;
    }

    /// <summary>
    /// L-05: <c>MemoryExtensions.Sort(keys, items)</c> with matching lengths (Seed 0), mismatched lengths (Seed 1,
    /// throwing <see cref="ArgumentException"/> on both Managed and Native), and a comparer that traps (Seed 2,
    /// throwing <see cref="InvalidOperationException"/> on both Managed and Native).
    /// </summary>
    [GpuKernel]
    public static void MemoryExtensionsSortExceptionKernel(
        int[] keysBacking,
        int[] itemsBacking,
        int[] keyLens,
        int[] itemLens,
        int[] modes,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int sliceOffset = t * SliceWidth;
        Span<int> keys = keysBacking.AsSpan().Slice(sliceOffset, keyLens[t]);
        Span<int> items = itemsBacking.AsSpan().Slice(sliceOffset, itemLens[t]);
        int mode = modes[t];

        if (mode <= 1)
        {
            keys.Sort(items);
        }
        else
        {
            keys.Sort((a, b) =>
            {
                Span<int> empty = default;
                int trapVal = empty.AsStackEnumerable().First();
                int cmp = a < b ? -1 : (a > b ? 1 : 0);
                return cmp + trapVal;
            });
        }

        int baseIdx = t * 4;
        if (keys.Length >= SliceWidth && items.Length >= SliceWidth)
        {
            dst[baseIdx + 0] = keys[0];
            dst[baseIdx + 1] = keys[SliceWidth - 1];
            dst[baseIdx + 2] = items[0];
            dst[baseIdx + 3] = items[SliceWidth - 1];
        }
    }

    /// <summary>
    /// L-06: Post-trap non-continuation witness (S30 / #729). When <c>First()</c> traps on an empty <c>Where</c>
    /// sequence, execution must abort before reaching <c>100 / denom</c> (which would divide by 0 if <c>First()</c>
    /// returned dummy <c>0</c> and continued).
    /// </summary>
    [GpuKernel]
    public static void TrapNonContinuationGuardKernel(
        int[] backing,
        int[] thresholds,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> slice = backing.AsSpan().Slice(t * SliceWidth, SliceWidth);
        int threshold = thresholds[t];
        int denom = slice.AsStackEnumerable().Where(x => x > threshold).First();
        dst[t] = 100 / denom;
    }
}
