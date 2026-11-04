#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffNullablePairHolder
{
    public int? First;
    public int? Second;
    public int Tag;
}

/// <summary>
/// Differential kernels (<c>Nullable&lt;T&gt;</c> / <c>T?</c> representation, <c>HasValue</c>,
/// <c>Value</c>, null-coalescing <c>??</c>, compound <c>??=</c>, type promotion in <c>??</c>, and null comparisons of empty
/// spans and nullable array-like structs).
/// </summary>
public static partial class NullableDifferentialKernels
{
    public static readonly string[] CoalesceAndSideEffectsColumns =
    [
        "coalesced_val",
        "side_counter",
        "chained_val",
        "holder_first_val",
        "holder_second_val",
        "assign_counter",
        "has_flags",
    ];

    public static readonly string[] DeviceFunctionsAndKleeneColumns =
    [
        "lifted_add_has",
        "lifted_add_val",
        "kleene_and_code",
        "kleene_or_code",
    ];

    public static readonly string[] FloatCoalesceWithNaNColumns =
    [
        "coalesced_float",
    ];

    public static readonly string[] CoalesceWiderWithNarrowerColumns =
    [
        "int_coalesce_short_val",
        "int_coalesce_ushort_val",
    ];

    [GpuDeviceFunction]
    public static int? ProduceNullableWithSideEffect(int hasVal, int val, ref int counter)
    {
        counter += 1;
        int? result = default;
        if (hasVal != 0)
        {
            result = val;
        }

        return result;
    }

    [GpuDeviceFunction]
    public static int ProduceFallbackWithSideEffect(int fallback, ref int counter)
    {
        counter += 10;
        return fallback;
    }

    [GpuDeviceFunction]
    public static int? HelperLiftedAdd(int? a, int? b)
    {
        int? res = default;
        if (a.HasValue && b.HasValue)
        {
            res = a.Value + b.Value;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static bool? HelperKleeneAnd(bool? a, bool? b)
    {
        bool? res = default;
        if (a.HasValue && !a.Value)
        {
            res = false;
        }
        else if (b.HasValue && !b.Value)
        {
            res = false;
        }
        else if (a.HasValue && b.HasValue)
        {
            res = true;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static bool? HelperKleeneOr(bool? a, bool? b)
    {
        bool? res = default;
        if (a.HasValue && a.Value)
        {
            res = true;
        }
        else if (b.HasValue && b.Value)
        {
            res = true;
        }
        else if (a.HasValue && b.HasValue)
        {
            res = false;
        }

        return res;
    }

    [GpuKernel]
    public static void NullableCoalesceAndSideEffects(
        int[] hasA,
        int[] valsA,
        int[] hasB,
        int[] valsB,
        int[] fallbacks,
        int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= hasA.Length)
        {
            return;
        }

        int f = fallbacks[tid];

        // 1. Null-coalescing `a ?? fallback` with side effects in both `a` and `fallback`:
        //    `a` must be evaluated exactly once; `fallback` must be evaluated only when `!a.HasValue`.
        int sideCounter = 0;
        int coalesced1 = ProduceNullableWithSideEffect(hasA[tid], valsA[tid], ref sideCounter)
                         ?? ProduceFallbackWithSideEffect(f, ref sideCounter);

        // 2. Chained `a ?? b ?? f` where `a` and `b` are `int?`:
        int? a = default;
        if (hasA[tid] != 0)
        {
            a = valsA[tid];
        }

        int? b = default;
        if (hasB[tid] != 0)
        {
            b = valsB[tid];
        }

        int? chainedNullable = a ?? b;
        int chainedVal = (a ?? b) ?? f;

        // 3. `??=` compound assignment on struct fields:
        DiffNullablePairHolder holder = new DiffNullablePairHolder
        {
            First = a,
            Second = b,
            Tag = 5
        };
        int assignCounter = 0;
        holder.First ??= ProduceFallbackWithSideEffect(f + 1, ref assignCounter);
        holder.Second ??= ProduceFallbackWithSideEffect(f + 2, ref assignCounter);

        int hasFlags = (a.HasValue ? 1 : 0)
                       | (b.HasValue ? 2 : 0)
                       | (chainedNullable.HasValue ? 4 : 0)
                       | (holder.First.HasValue ? 8 : 0)
                       | (holder.Second.HasValue ? 16 : 0);

        int o = tid * 7;
        r[o + 0] = coalesced1;
        r[o + 1] = sideCounter;
        r[o + 2] = chainedVal;
        r[o + 3] = holder.First ?? -999;
        r[o + 4] = holder.Second ?? -999;
        r[o + 5] = assignCounter;
        r[o + 6] = hasFlags;
    }

    [GpuKernel]
    public static void NullableDeviceFunctionsAndKleeneLogic(
        int[] hasA,
        int[] valsA,
        int[] hasB,
        int[] valsB,
        int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= hasA.Length)
        {
            return;
        }

        int? a = default;
        if (hasA[tid] != 0)
        {
            a = valsA[tid];
        }

        int? b = default;
        if (hasB[tid] != 0)
        {
            b = valsB[tid];
        }

        int? sum = HelperLiftedAdd(a, b);
        int sumVal = sum ?? -12345;

        bool? ba = default;
        if (hasA[tid] != 0)
        {
            ba = (valsA[tid] & 1) != 0;
        }

        bool? bb = default;
        if (hasB[tid] != 0)
        {
            bb = (valsB[tid] & 1) != 0;
        }

        bool? kAnd = HelperKleeneAnd(ba, bb);
        bool? kOr = HelperKleeneOr(ba, bb);

        int andCode = kAnd.HasValue ? (kAnd.Value ? 2 : 1) : 0;
        int orCode = kOr.HasValue ? (kOr.Value ? 2 : 1) : 0;

        int o = tid * 4;
        r[o + 0] = sum.HasValue ? 1 : 0;
        r[o + 1] = sumVal;
        r[o + 2] = andCode;
        r[o + 3] = orCode;
    }

    [GpuKernel]
    public static void NullableFloatCoalesceWithNaN(int[] hasA, float[] valsA, float[] fallbacks, float[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= hasA.Length)
        {
            return;
        }

        // A `float?` wrapping `NaN` has `HasValue == true`, so `a ?? fallback` must return `NaN`, not `fallback`.
        float? a = default;
        if (hasA[tid] != 0)
        {
            a = valsA[tid];
        }

        r[tid] = a ?? fallbacks[tid];
    }

    /// <summary>
    /// Reproduces #998: Null-coalescing <c>(a ?? b) ?? fallback</c> where <c>a</c> is <c>int?</c> and
    /// <c>b</c> is a narrower nullable integer type (<c>short?</c> or <c>ushort?</c>) that implicitly widens to
    /// <c>int?</c> in C#. In C#, <c>a ?? b</c> has type <c>int?</c> and preserves the full 32-bit signed value of
    /// <c>a</c>. In G# (<c>NullRepresentation.AppendCoalesce</c>), the raw C++ ternary
    /// <c>__gsharp_coalesce_tmp.HasValue ? __gsharp_coalesce_tmp.Value : b</c> has true branch <c>int</c> and false
    /// branch <c>System_Nullable&lt;short&gt;</c> (or <c>System_Nullable&lt;unsigned short&gt;</c>); C++ converts the
    /// <c>int</c> true branch into the narrower <c>System_Nullable&lt;...&gt;</c>, truncating <c>a.Value</c> to 16 bits!
    /// </summary>
    [GpuKernel]
    public static void CoalesceWiderNullableWithNarrowerNullable(
        int[] hasA,
        int[] valsA,
        int[] hasB,
        short[] valsB,
        ushort[] uValsB,
        int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= hasA.Length)
        {
            return;
        }

        int? a = default;
        if (hasA[tid] != 0)
        {
            a = valsA[tid];
        }

        short? b = default;
        ushort? ub = default;
        if (hasB[tid] != 0)
        {
            b = valsB[tid];
            ub = uValsB[tid];
        }

        int o = tid * 2;
        r[o + 0] = (a ?? b) ?? -1;
        r[o + 1] = (a ?? ub) ?? -1;
    }

    // -------------------------------------------------------------------------
    // #998: Empty stack-allocated span local (stackalloc int[0] and []) compared to null
    // In C#, stackalloc int[0] and [] produce an empty Span<int> where (s == null) is true and (s != null) is false.
    // G#'s NullRepresentation.AppendArrayNullTest checks StackArrayLocals.IsStackArrayReference without checking
    // length == 0, emitting the constant 'false' for (s == null) and 'true' for (s != null).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void EmptyStackSpanNullComparisonReproKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        Span<int> sZero = stackalloc int[0];
        Span<int> sCol = [];
        Span<int> sDef = default;

        int col = t * 4;
        dst[col + 0] = sZero == null ? 1 : 0;
        dst[col + 1] = sZero != null ? 1 : 0;
        dst[col + 2] = sCol == null ? 1 : 0;
        dst[col + 3] = (sCol != null ? 10 : 0) + (sDef == null ? 1 : 0) + (a[t] & 0);
    }

    // -------------------------------------------------------------------------
    // #998: Nullable array-like struct (Memory<int>?, ArraySegment<int>?) wrapping default(T)
    // In C#, converting default(Memory<int>) or default(ArraySegment<int>) to Memory<int>? / ArraySegment<int>?
    // constructs a Nullable<T> with HasValue == true, so `is null` and `== null` are false, while `is not null`,
    // `!= null`, and `.HasValue` are true.
    // G# erases Nullable<T> for array-like value types to GSharpArray<T> (UnwrapNullableArrayLike) and tests
    // `.data == nullptr`, which evaluates to true for default(Memory<int>) / default(ArraySegment<int>).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NullableArrayLikeDefaultValueNullCheckReproKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        Memory<int> emptyMem = default;
        Memory<int>? nonNullEmptyMem = emptyMem;

        ArraySegment<int> emptySeg = default;
        ArraySegment<int>? nonNullEmptySeg = emptySeg;

        int col = t * 4;
        dst[col + 0] = nonNullEmptyMem is null ? 1 : 0;
        dst[col + 1] = nonNullEmptyMem is not null ? 1 : 0;
        dst[col + 2] = (nonNullEmptyMem == null ? 1 : 0) + (nonNullEmptyMem != null ? 10 : 0) + (nonNullEmptyMem.HasValue ? 100 : 0);
        dst[col + 3] = (nonNullEmptySeg is null ? 1 : 0) + (nonNullEmptySeg is not null ? 10 : 0) + (nonNullEmptySeg.HasValue ? 100 : 0) + (a[t] & 0);
    }

    // -------------------------------------------------------------------------
    // #998: Null-coalescing (??) on narrow Nullable<T> (byte?, ushort?, sbyte?) under var
    // In C#, `mb ?? 0` (where mb is byte? and 0 is a constant in 0..255) has type byte, `mus ?? 0` has type ushort,
    // and `mb ?? usv` (byte? ?? ushort) has type ushort.
    // NullRepresentation.AppendCoalesce emits `return tmp.HasValue ? tmp.Value : right;` in an untyped lambda, where
    // C++ `?:` promotes `unsigned char` / `unsigned short` mixed with `int` or with each other to `int`. Under `var`,
    // incrementing overflows past 255/65535 into int instead of wrapping in byte/ushort.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void NarrowNullableCoalesceVarPromotionReproKernel(int[] conds, byte[] ba, ushort[] usa, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int c = conds[t];
        byte? mb = (c & 1) == 0 ? new byte?(255) : new byte?(ba[t]);
        var x = mb ?? 0;
        x++;

        ushort? mus = (c & 2) == 0 ? new ushort?(65535) : new ushort?(usa[t]);
        var y = mus ?? 0;
        y++;

        byte? mbNullForMax = (c & 4) == 0 ? new byte?() : new byte?(255);
        ushort usMax = 65535;
        var z = mbNullForMax ?? usMax;
        z++;

        int col = t * 3;
        dst[col + 0] = x;
        dst[col + 1] = y;
        dst[col + 2] = z;
    }
}
