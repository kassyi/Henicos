#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct M1NullablePairHolder
{
    public int? First;
    public int? Second;
    public int Tag;
}

/// <summary>
/// Differential kernels (<c>Nullable&lt;T&gt;</c> / <c>T?</c> representation, <c>HasValue</c>,
/// <c>Value</c>, null-coalescing <c>??</c>, compound <c>??=</c>, and type promotion in <c>??</c>).
/// </summary>
public static partial class M1DiffNullableKernels
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
        M1NullablePairHolder holder = new M1NullablePairHolder
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
    /// Reproduces Finding U5-M1-3: Null-coalescing <c>(a ?? b) ?? fallback</c> where <c>a</c> is <c>int?</c> and
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
}
