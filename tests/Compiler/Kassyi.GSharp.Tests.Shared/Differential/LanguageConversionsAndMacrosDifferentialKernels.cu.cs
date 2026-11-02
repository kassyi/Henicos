#nullable enable
using System;
using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Core.Gpu;
using static Kassyi.GSharp.Core.Warp;
using static System.Math;
using static System.Numerics.BitOperations;
using GpuAlias = Kassyi.GSharp.Core.Gpu;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A wrapper struct whose <c>implicit operator</c> is an identity copy of its first field.
/// </summary>
public struct DiffIdentityWrap
{
    public int Value;

    public static implicit operator DiffIdentityWrap(int v) =>
        new DiffIdentityWrap { Value = v };

    public static implicit operator int(DiffIdentityWrap w) =>
        w.Value;
}

/// <summary>
/// A struct whose <c>implicit operator</c> scales and offsets the value (#963: C++ <c>WriteImplicitConversionOperator</c>
/// emits a raw first-field copy and ignores the C# operator body).
/// </summary>
public struct DiffLinearMeter
{
    public int Units;

    public static implicit operator DiffLinearMeter(int v) =>
        new DiffLinearMeter { Units = (v * 3) + 5 };

    public static implicit operator int(DiffLinearMeter m) =>
        (m.Units * 2) + 7;
}

public struct DiffMacroContext
{
    public int Bias;
    public int Calls;
}

public struct DiffMacroNum
{
    public int Value;

    public DiffMacroNum(int value) => Value = value;

    [GpuOperatorMacro(typeof(DiffMacroNumOps), nameof(DiffMacroNumOps.AddWithContext))]
    public static DiffMacroNum operator +(DiffMacroNum left, DiffMacroNum right) =>
        throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(DiffMacroSecretOps), "PrivateSubtract")]
    public static DiffMacroNum operator -(DiffMacroNum left, DiffMacroNum right) =>
        throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(DiffMacroNumOps), nameof(DiffMacroNumOps.ShiftRight))]
    public static DiffMacroNum operator >>(DiffMacroNum value, int shift) =>
        throw new GpuOnlyException();
}

public static partial class DiffMacroNumOps
{
    [GpuDeviceFunction]
    public static void AddWithContext(
        ref DiffMacroContext ctx,
        out DiffMacroNum result,
        DiffMacroNum left,
        DiffMacroNum right)
    {
        ctx.Calls += 1;
        result = new DiffMacroNum((left.Value * 3) + right.Value + ctx.Bias);
    }

    // Comes first in declaration order: float overload adds 5000, whereas C# overload resolution for `int` shift
    // prefers `long` over `float` (#478).
    [GpuDeviceFunction]
    public static void ShiftRight(out DiffMacroNum result, DiffMacroNum value, float shift)
    {
        result = new DiffMacroNum(value.Value + 5000);
    }

    [GpuDeviceFunction]
    public static void ShiftRight(out DiffMacroNum result, DiffMacroNum value, long shift)
    {
        result = new DiffMacroNum(value.Value >> (int)shift);
    }
}

public static partial class DiffMacroSecretOps
{
    [GpuDeviceFunction]
    private static void PrivateSubtract(ref DiffMacroNum result, DiffMacroNum left, DiffMacroNum right)
    {
        result.Value = (left.Value * 2) - right.Value + 9;
    }
}

public static partial class LocalGpuShadow
{
    [GpuDeviceFunction]
    public static void SyncThreads(ref int acc)
    {
        acc += 333;
    }
}

/// <summary>
/// Differential kernels for user-defined conversion operators (#963), <c>[GpuOperatorMacro]</c> expansion
/// (direct, inside <c>[GpuDeviceFunction]</c>, across barriers, nested evaluation order, private target #711,
/// overload resolution #478), and <c>using static</c> / type alias directives (#503) (Lane D6 Stage 2).
/// </summary>
public static partial class LanguageConversionsAndMacrosDifferentialKernels
{
    public static readonly string[] ImplicitConversionsColumns =
    [
        "identity_wrap_val",
        "identity_roundtrip_int",
        "identity_fn_arg",
        "identity_ternary",
        "linear_meter_explicit_ctor",
        "linear_meter_from_int",
        "int_from_linear_meter",
        "fn_arg_implicit_linear_meter",
    ];

    public static readonly string[] OperatorMacrosColumns =
    [
        "direct_add",
        "direct_private_sub",
        "nested_right_assoc",
        "nested_left_assoc",
        "nested_eval_order_val",
        "nested_eval_order_trace",
        "parenthesized_cast_macro",
        "shift_overload_long_wins",
        "helper_fn_macro_and_ctx_calls",
    ];

    public static readonly string[] MacroAcrossBarrierAndUsingStaticColumns =
    [
        "suspended_macro_val",
        "suspended_ctx_calls",
        "neighbor_after_unqualified_sync",
        "warp_popc_and_math_clamp",
        "local_shadow_sync_acc",
    ];

    [GpuDeviceFunction]
    public static int AcceptIdentityWrap(DiffIdentityWrap w) => (w.Value * 2) + 1;

    [GpuDeviceFunction]
    public static int AcceptLinearMeter(DiffLinearMeter m) => m.Units + 10;

    [GpuDeviceFunction]
    public static DiffMacroNum MakeOperandWithStep(ref int trace, int stepId, int val)
    {
        trace = (trace * 10) + stepId;
        return new DiffMacroNum(val + stepId);
    }

    [GpuDeviceFunction]
    public static int EvalMacrosInDeviceFunction(ref DiffMacroContext ctx, int x, int y)
    {
        var a = new DiffMacroNum(x);
        var b = new DiffMacroNum(y);
        DiffMacroNum sum = default;
        DiffMacroNum diff = default;
        sum = a + b;
        diff = sum - a;
        return diff.Value;
    }

    [GpuDeviceFunction]
    public static int SuspendAndCombineMacro(int[] scratch, int localTid, int x, int y, out int totalCalls)
    {
        DiffMacroContext ctx = new DiffMacroContext { Bias = 7, Calls = 0 };
        var a = new DiffMacroNum(x);
        var b = new DiffMacroNum(y);
        DiffMacroNum pre = default;
        pre = a + b;

        scratch[localTid] = pre.Value;
        SyncThreads();

        int neighbor = scratch[(localTid + 1) % (int)BlockDimension.X];
        GpuAlias.SyncThreads();

        var n = new DiffMacroNum(neighbor);
        DiffMacroNum post = default;
        post = pre - n;
        totalCalls = ctx.Calls;
        return post.Value;
    }

    [GpuKernel]
    public static void UserDefinedImplicitConversionsKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 8;

        // 1. Identity first-field implicit operator works identically in C# and C++
        DiffIdentityWrap idWrap = x;
        int idRoundtrip = idWrap;
        int idFnArg = AcceptIdentityWrap(y);
        DiffIdentityWrap idTernary = (x & 1) == 0 ? idWrap : y;

        // 2. Non-trivial implicit operator (#963): scales/offsets in C#, raw first-field copy in C++
        var explicitMeter = new DiffLinearMeter { Units = (x * 3) + 5 };
        DiffLinearMeter implicitMeter = x;
        int implicitBack = explicitMeter;
        int implicitFnArg = AcceptLinearMeter(y);

        r[o + 0] = idWrap.Value;
        r[o + 1] = idRoundtrip;
        r[o + 2] = idFnArg;
        r[o + 3] = idTernary.Value;
        r[o + 4] = explicitMeter.Units;
        r[o + 5] = implicitMeter.Units;
        r[o + 6] = implicitBack;
        r[o + 7] = implicitFnArg;
    }

    [GpuKernel]
    public static void OperatorMacrosAndEvaluationOrderKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0xFFF;
        int y = ys[tid] & 0xFFF;
        int z = (x ^ y) & 0x3FF;
        int o = tid * 9;

        DiffMacroContext ctx = new DiffMacroContext { Bias = 5, Calls = 0 };
        var a = new DiffMacroNum(x);
        var b = new DiffMacroNum(y);
        var d = new DiffMacroNum(z);

        DiffMacroNum cAdd = default;
        cAdd = a + b;

        DiffMacroNum cSub = default;
        cSub = a - b;

        DiffMacroNum cRight = default;
        cRight = a + (b - d);

        DiffMacroNum cLeft = default;
        cLeft = (a - b) + d;

        int orderTrace = 0;
        DiffMacroNum cOrder = default;
        cOrder = MakeOperandWithStep(ref orderTrace, 1, x) +
                 (MakeOperandWithStep(ref orderTrace, 2, y) - MakeOperandWithStep(ref orderTrace, 3, z));

        DiffMacroNum cCast = default;
        cCast = (DiffMacroNum)(a + b);

        DiffMacroNum cShift = default;
        cShift = a >> 2;

        int helperVal = EvalMacrosInDeviceFunction(ref ctx, x, y);

        r[o + 0] = cAdd.Value;
        r[o + 1] = cSub.Value;
        r[o + 2] = cRight.Value;
        r[o + 3] = cLeft.Value;
        r[o + 4] = cOrder.Value;
        r[o + 5] = orderTrace;
        r[o + 6] = cCast.Value;
        r[o + 7] = cShift.Value;
        r[o + 8] = (helperVal * 10) + ctx.Calls;
    }

    [GpuKernel]
    public static void OperatorMacroAcrossBarrierAndUsingStaticKernel(int[] xs, int[] scratch, int[] r)
    {
        int localTid = ThreadIndex.X;
        int globalTid = BlockIndex.X * BlockDimension.X + localTid;
        if (globalTid >= xs.Length)
        {
            return;
        }

        int x = xs[globalTid] & 0xFFF;
        int y = (x * 3) & 0x7FF;

        int suspendedVal = SuspendAndCombineMacro(scratch, localTid, x, y, out int macroCalls);

        // Unqualified SyncThreads() from `using static Kassyi.GSharp.Core.Gpu;`
        scratch[localTid] = x + localTid;
        SyncThreads();
        int neighbor = scratch[(localTid + 3) % (int)BlockDimension.X];
        SyncThreads();

        // Unqualified ShuffleSync() from `using static Kassyi.GSharp.Core.Warp;`, LeadingZeroCount() from `using static System.Numerics.BitOperations;`, and Abs/Clamp from `using static System.Math;`
        int laneZeroX = ShuffleSync(0xFFFF_FFFFu, x, 0);
        int lzc = LeadingZeroCount((uint)x | 1u);
        int clamped = Clamp(Abs(x - y), 0, 500);

        // User helper method named SyncThreads(ref int) must NOT be misclassified as a GPU barrier (#503)
        int shadowAcc = 10;
        LocalGpuShadow.SyncThreads(ref shadowAcc);

        int o = globalTid * 5;
        r[o + 0] = suspendedVal;
        r[o + 1] = macroCalls;
        r[o + 2] = neighbor;
        r[o + 3] = (lzc * 1000) + clamped + laneZeroX;
        r[o + 4] = shadowAcc;
    }
}
