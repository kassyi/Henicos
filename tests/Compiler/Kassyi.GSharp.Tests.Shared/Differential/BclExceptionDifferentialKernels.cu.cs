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
/// Differential kernels for runtime checks and BCL exceptions: <see cref="Nullable{T}.Value"/> (<see cref="InvalidOperationException"/>,
/// #718), <c>Math.Clamp</c> (<see cref="ArgumentException"/> when <c>min &gt; max</c>, #718), <c>StackLinq</c> empty and
/// multi-element sequence traps (<see cref="InvalidOperationException"/> and <see cref="ArgumentOutOfRangeException"/>),
/// <c>ElementAtOrDefault(-1)</c> non-Span enumeration side effects (#1060), <c>MemoryExtensions.Sort</c> length mismatch
/// and comparer exception wrapping, and post-trap non-continuation before <c>100 / denom</c> (#729).
/// </summary>
public static partial class BclExceptionDifferentialKernels
{
    public const int SliceWidth = 4;

    public static readonly string[] NullableInt32ValueColumns =
    [
        "int_val",
        "int_default",
        "int_coalesce",
    ];

    public static readonly string[] NullableStructValueColumns =
    [
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
        "res",
    ];

    public static readonly string[] StackLinqElementAtColumns =
    [
        "res",
    ];

    public static readonly string[] SortExceptionColumns =
    [
        "k0",
        "k3",
        "v0",
        "v3",
    ];

    /// <summary>
    /// <see cref="Nullable{T}.Value"/> on <c>int?</c> when <c>HasValue</c> is
    /// <see langword="true"/> (Seed 0) vs <see langword="false"/> (Seed 1, throwing
    /// <see cref="InvalidOperationException"/> on C# paths, #718).
    /// </summary>
    [GpuKernel]
    public static void NullableInt32ValueAccessKernel(
        int[] hasInt,
        int[] intVals,
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

#pragma warning disable CS8629 // Deliberate explicit .Value access on nullable to verify InvalidOperationException when null
        int valInt = optInt.Value;
#pragma warning restore CS8629

        int baseIdx = t * 3;
        dst[baseIdx + 0] = valInt;
        dst[baseIdx + 1] = defInt;
        dst[baseIdx + 2] = coalescedInt;
    }

    /// <summary>
    /// <see cref="Nullable{T}.Value"/> on user-defined <c>DiffNullablePairStruct?</c> when <c>HasValue</c> is
    /// <see langword="true"/> (Seed 0) vs <see langword="false"/> (Seed 1, throwing
    /// <see cref="InvalidOperationException"/> on C# paths, #718).
    /// </summary>
    [GpuKernel]
    public static void NullableStructValueAccessKernel(
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

        DiffNullablePairStruct? optPair = default;
        if (hasPair[t] != 0)
        {
            optPair = new DiffNullablePairStruct { X = pairX[t], Y = pairY[t] };
        }

        DiffNullablePairStruct? emptyPair = default;
        DiffNullablePairStruct coalescedPair = emptyPair ?? new DiffNullablePairStruct { X = -555, Y = -444 };
        int defPairX = coalescedPair.X;

#pragma warning disable CS8629 // Deliberate explicit .Value access on nullable to verify InvalidOperationException when null
        DiffNullablePairStruct valPair = optPair.Value;
#pragma warning restore CS8629

        int baseIdx = t * 2;
        dst[baseIdx + 0] = valPair.X + valPair.Y;
        dst[baseIdx + 1] = defPairX;
    }

    /// <summary>
    /// <see cref="Math.Clamp(int, int, int)"/> (<see cref="ArgumentException"/> when <c>min &gt; max</c>, #718).
    /// </summary>
    [GpuKernel]
    public static void MathClampInt32Kernel(
        int[] vals,
        int[] mins,
        int[] maxs,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(vals[t], mins[t], maxs[t]);
    }

    /// <summary>
    /// <see cref="Math.Clamp(float, float, float)"/> (<see cref="ArgumentException"/> when <c>min &gt; max</c>, #718).
    /// </summary>
    [GpuKernel]
    public static void MathClampSingleKernel(
        float[] vals,
        float[] mins,
        float[] maxs,
        float[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(vals[t], mins[t], maxs[t]);
    }

    /// <summary>
    /// <see cref="Math.Clamp(double, double, double)"/> (<see cref="ArgumentException"/> when <c>min &gt; max</c>, #718).
    /// </summary>
    [GpuKernel]
    public static void MathClampDoubleKernel(
        double[] vals,
        double[] mins,
        double[] maxs,
        double[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = Math.Clamp(vals[t], mins[t], maxs[t]);
    }

    /// <summary>StackLinq <c>First</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqFirstKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().First();
    }

    /// <summary>StackLinq <c>Last</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqLastKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().Last();
    }

    /// <summary>StackLinq <c>Single</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqSingleKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().Single();
    }

    /// <summary>StackLinq <c>Min</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqMinKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().Min();
    }

    /// <summary>StackLinq <c>Max</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqMaxKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().Max();
    }

    /// <summary>StackLinq <c>Average</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqAverageKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = (int)backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().Average();
    }

    /// <summary>StackLinq <c>Aggregate</c> terminal operator.</summary>
    [GpuKernel]
    public static void StackLinqAggregateKernel(int[] backing, int[] lengths, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = backing.AsSpan().Slice(t * SliceWidth, lengths[t]).AsStackEnumerable().Aggregate((a, b) => a + b);
    }

    /// <summary>StackLinq <c>ElementAt</c> operator.</summary>
    [GpuKernel]
    public static void StackLinqElementAtKernel(int[] backing, int[] indices, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> slice = backing.AsSpan().Slice(t * SliceWidth, SliceWidth);
        dst[t] = slice.AsStackEnumerable().ElementAt(indices[t]);
    }

    /// <summary>
    /// <c>ElementAtOrDefault(-1)</c> on non-Span <c>Select</c> and <c>Where</c> sequences (#1060).
    /// </summary>
    [GpuKernel]
    public static void ElementAtOrDefaultNegativeIndexSideEffectKernel(
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
    /// <c>MemoryExtensions.Sort(keys, items)</c> with mismatched lengths throwing <see cref="ArgumentException"/>.
    /// </summary>
    [GpuKernel]
    public static void MemoryExtensionsSortLengthMismatchKernel(
        int[] keysBacking,
        int[] itemsBacking,
        int[] itemLens,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int sliceOffset = t * SliceWidth;
        Span<int> keys = keysBacking.AsSpan().Slice(sliceOffset, SliceWidth);
        Span<int> items = itemsBacking.AsSpan().Slice(sliceOffset, itemLens[t]);

        keys.Sort(items);

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
    /// <c>MemoryExtensions.Sort</c> with a comparer that traps, throwing <see cref="InvalidOperationException"/>.
    /// </summary>
    [GpuKernel]
    public static void MemoryExtensionsSortComparerTrapKernel(
        int[] keysBacking,
        int[] trapTriggers,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int sliceOffset = t * SliceWidth;
        Span<int> keys = keysBacking.AsSpan().Slice(sliceOffset, SliceWidth);
        int trigger = trapTriggers[t];

        keys.Sort((a, b) =>
        {
            if (trigger != 0)
            {
                Span<int> empty = default;
                int trapVal = empty.AsStackEnumerable().First();
                int cmp = a < b ? -1 : (a > b ? 1 : 0);
                return cmp + trapVal;
            }

            return a < b ? -1 : (a > b ? 1 : 0);
        });

        int baseIdx = t * 4;
        if (keys.Length >= SliceWidth)
        {
            dst[baseIdx + 0] = keys[0];
            dst[baseIdx + 1] = keys[SliceWidth - 1];
            dst[baseIdx + 2] = 0;
            dst[baseIdx + 3] = 0;
        }
    }

    /// <summary>
    /// Post-trap non-continuation witness (#729). When <c>First()</c> traps on an empty <c>Where</c>
    /// sequence, execution must abort before reaching <c>100 / denom</c>.
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
