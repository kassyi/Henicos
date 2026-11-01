#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct M1StructCounter
{
    public int Value;

    public int ComputedDoubled => Value * 2;

    public int Addend
    {
        get => Value;
        set => Value += value;
    }

    public int ResetTo
    {
        get => Value;
        set => this = new M1StructCounter { Value = value };
    }
}

public struct M1StructReadonlyHolder
{
    public readonly M1StructCounter ReadonlyInner;
    public M1StructCounter MutableInner;

    public M1StructReadonlyHolder(int roVal, int mutVal)
    {
        ReadonlyInner = new M1StructCounter { Value = roVal };
        MutableInner = new M1StructCounter { Value = mutVal };
    }
}

public struct M1StructPropHolder
{
    public M1StructCounter AutoProp { get; set; }
    public M1StructCounter Backing;

    public M1StructCounter ComputedProp
    {
        get => Backing;
        set => Backing = value;
    }
}

public struct M1StructWithExplicitParameterlessCtor
{
    public int A = 10;
    public int B { get; set; } = 20;
    public int C;

    public M1StructWithExplicitParameterlessCtor()
    {
        C = A + B + 5;
    }

    public M1StructWithExplicitParameterlessCtor(int b)
    {
        B = b;
        C = A + B;
    }

    public M1StructWithExplicitParameterlessCtor(int a, int b, bool earlyReturn) : this()
    {
        A = a;
        if (earlyReturn)
        {
            return;
        }

        B = b;
        C = (A * 10) + B;
    }
}

public struct M1StructWithFieldInitAndParamCtorOnly
{
    public int A = 15;
    public int B { get; set; } = 25;
    public int C;

    public M1StructWithFieldInitAndParamCtorOnly(int c)
    {
        C = A + B + c;
    }
}

public record struct M1RecordPoint(int X, int Y, int Z)
{
    public int Tag { get; set; } = 7;

    public int Sum => X + Y + Z + Tag;
}

public struct M1ScaledInt
{
    public int Value;

    public static implicit operator M1ScaledInt(int v) =>
        new M1ScaledInt { Value = (v * 4) + 3 };

    public static implicit operator int(M1ScaledInt s) =>
        s.Value + 100;
}

/// <summary>
/// Differential kernels (<c>struct</c> value semantics, constructors, <c>in</c> parameters,
/// <c>readonly</c> fields, <c>with</c> expressions, and conversion operators).
/// </summary>
public static partial class M1DiffStructKernels
{
    public static readonly string[] MutationAndCopyColumns =
    [
        "local_val",
        "assigned_copy_val",
        "by_val_ret",
        "resettable_val",
        "in_helper_ret",
        "caller_struct_val",
        "ro_inner_val",
        "mut_inner_val",
        "auto_prop_unchanged",
        "computed_prop_doubled",
    ];

    public static readonly string[] DefaultVsNewColumns =
    [
        "explicit_default_sum",
        "explicit_new0_a",
        "explicit_new0_b",
        "explicit_new0_c",
        "explicit_new1_c",
        "explicit_new2_b",
        "explicit_new2_c",
        "explicit_init_b",
        "explicit_init_c",
        "paramonly_default_and_new0",
        "paramonly_new1_c",
    ];

    public static readonly string[] RecordWithColumns =
    [
        "p0_sum",
        "p1_x",
        "p1_y",
        "p1_tag",
        "p2_x",
        "p2_y",
        "side_effect",
        "p3_x",
        "p3_y",
        "p3_z",
        "p3_tag",
    ];

    public static readonly string[] AutoPropInDefensiveCopyColumns =
    [
        "in_snapshot_after_holder_mutated",
        "holder_after_mutation",
    ];

    public static readonly string[] UserDefinedImplicitConvColumns =
    [
        "scaled_field_val",
        "converted_back_int",
    ];

    [GpuDeviceFunction]
    public static int MutateLocalAndReturn(M1StructCounter byValue, ref M1StructCounter byRef, int delta)
    {
        byValue.Addend = delta;
        byRef.Addend = delta * 2;
        return byValue.Value;
    }

    [GpuDeviceFunction]
    public static int ReadInCopyMutatedLocally(in M1StructCounter inCounter, int delta)
    {
        M1StructCounter localCopy = inCounter;
        localCopy.Addend = delta;
        return localCopy.Value + inCounter.Value + inCounter.ComputedDoubled;
    }

    [GpuDeviceFunction]
    public static int ReadInAfterMutatingPropHolder(in M1StructCounter inSnapshot, ref M1StructPropHolder holder, int newValue)
    {
        holder.AutoProp = new M1StructCounter { Value = newValue };
        return inSnapshot.Value;
    }

    [GpuKernel]
    public static void StructMutationAndCopySemantics(int[] inputs, int[] deltas, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid];
        int d = deltas[tid];

        // 1. Mutable local struct variable: mutating local via setter and ref helper mutates the local,
        //    while value assignment (assignedCopy = local) does not mutate the original.
        M1StructCounter local = new M1StructCounter { Value = v };
        M1StructCounter assignedCopy = local;
        assignedCopy.Addend = d;
        local.Addend = 1;

        int byValResult = MutateLocalAndReturn(local, ref local, d);

        // 2. Setter that assigns `this = ...`
        M1StructCounter resettable = new M1StructCounter { Value = v };
        resettable.ResetTo = v + d;

        // 3. `in` struct parameter of a [GpuDeviceFunction] helper: mutating a copy inside the helper
        //    leaves the caller's struct untouched.
        M1StructCounter callerStruct = new M1StructCounter { Value = v };
        int inHelperResult = ReadInCopyMutatedLocally(in callerStruct, d);

        // 4. `readonly` struct field vs mutable struct field of an enclosing struct:
        M1StructReadonlyHolder roHolder = new M1StructReadonlyHolder(v, v + 1);
        int roRead = ReadInCopyMutatedLocally(in roHolder.ReadonlyInner, d);
        roHolder.MutableInner.Addend = d;

        // 5. Property getter returning a struct: reading computed getter on property return value,
        //    and mutating a copy of the property return value does not mutate the holder.
        M1StructPropHolder propHolder = new M1StructPropHolder
        {
            AutoProp = new M1StructCounter { Value = v },
            Backing = new M1StructCounter { Value = v + 2 }
        };
        M1StructCounter fromAutoProp = propHolder.AutoProp;
        fromAutoProp.Addend = d;

        int o = tid * 10;
        r[o + 0] = local.Value;
        r[o + 1] = assignedCopy.Value;
        r[o + 2] = byValResult;
        r[o + 3] = resettable.Value;
        r[o + 4] = inHelperResult + roRead;
        r[o + 5] = callerStruct.Value;
        r[o + 6] = roHolder.ReadonlyInner.Value;
        r[o + 7] = roHolder.MutableInner.Value;
        r[o + 8] = propHolder.AutoProp.Value;
        r[o + 9] = propHolder.ComputedProp.ComputedDoubled;
    }

    [GpuKernel]
    public static void StructDefaultVsNewAndFieldInitializers(int[] inputs, int[] flags, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid];
        bool early = (flags[tid] & 1) != 0;

        // 1. default(S) vs new S() when S has an explicit parameterless constructor and field initializers:
        M1StructWithExplicitParameterlessCtor sDefault = default;
        M1StructWithExplicitParameterlessCtor sNew0 = new M1StructWithExplicitParameterlessCtor();
        M1StructWithExplicitParameterlessCtor sNew1 = new M1StructWithExplicitParameterlessCtor(v);
        M1StructWithExplicitParameterlessCtor sNew2 = new M1StructWithExplicitParameterlessCtor(v, v + 3, early);
        M1StructWithExplicitParameterlessCtor sNewInit = new M1StructWithExplicitParameterlessCtor { B = v + 9 };

        // 2. default(S) vs new S() vs new S(c) when S has field initializers and only a parameterized constructor:
        //    In C#, both default(S) and new S() produce all-zero fields (field initializers do NOT run on implicit new S()).
        M1StructWithFieldInitAndParamCtorOnly pDefault = default;
        M1StructWithFieldInitAndParamCtorOnly pImplicitNew = new M1StructWithFieldInitAndParamCtorOnly();
        M1StructWithFieldInitAndParamCtorOnly pParamNew = new M1StructWithFieldInitAndParamCtorOnly(v);

        int o = tid * 11;
        r[o + 0] = sDefault.A + sDefault.B + sDefault.C;
        r[o + 1] = sNew0.A;
        r[o + 2] = sNew0.B;
        r[o + 3] = sNew0.C;
        r[o + 4] = sNew1.C;
        r[o + 5] = sNew2.B;
        r[o + 6] = sNew2.C;
        r[o + 7] = sNewInit.B;
        r[o + 8] = sNewInit.C;
        r[o + 9] = pDefault.A + pDefault.B + pDefault.C + pImplicitNew.A + pImplicitNew.B + pImplicitNew.C;
        r[o + 10] = pParamNew.C;
    }

    [GpuKernel]
    public static void RecordStructWithExpressions(int[] xs, int[] ys, int[] zs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int z = zs[tid];

        M1RecordPoint p0 = new M1RecordPoint(x, y, z);

        // Basic `with` expression: original p0 is unchanged, copy p1 has updated X and Tag.
        M1RecordPoint p1 = p0 with { X = x + 10, Tag = 99 };

        // Side-effecting initializer expressions and reading another field of the receiver:
        int sideEffect = 1;
        M1RecordPoint p2 = p1 with
        {
            X = p1.Y + (++sideEffect),
            Y = p1.X + (sideEffect *= 3)
        };

        // Reassigning the receiver source variable inside an initializer expression:
        // The cloned receiver has already been copied before the member initializers run, so Z and Tag
        // come from `moving`'s original snapshot, while Y = moving.Y reads the newly assigned `moving`.
        M1RecordPoint moving = p0;
        M1RecordPoint replacement = new M1RecordPoint(100, 200, 300) { Tag = 400 };
        M1RecordPoint p3 = moving with
        {
            X = (moving = replacement).X + 1,
            Y = moving.Y + 2
        };

        int o = tid * 11;
        r[o + 0] = p0.Sum;
        r[o + 1] = p1.X;
        r[o + 2] = p1.Y;
        r[o + 3] = p1.Tag;
        r[o + 4] = p2.X;
        r[o + 5] = p2.Y;
        r[o + 6] = sideEffect;
        r[o + 7] = p3.X;
        r[o + 8] = p3.Y;
        r[o + 9] = p3.Z;
        r[o + 10] = p3.Tag;
    }

    /// <summary>
    /// Reproduces Finding U5-M1-1: Passing a struct auto-property (<c>holder.AutoProp</c>) to an <c>in</c> parameter
    /// of a <c>[GpuDeviceFunction]</c> while also passing <c>ref holder</c>.
    /// In C#, <c>holder.AutoProp</c> invokes the property getter and passes a defensive temporary copy by readonly
    /// reference, so mutating <c>holder.AutoProp</c> inside the callee does not alter the <c>in</c> parameter.
    /// In G# (<c>RefSyntaxHelper.IsLValueSymbol</c>), non-computed struct properties are classified as C++ lvalues,
    /// binding the <c>in</c> parameter directly to <c>&amp;holder.__gsharp_mb_AutoProp</c>.
    /// </summary>
    [GpuKernel]
    public static void AutoPropertyPassedToInParameterDefensiveCopy(int[] initialValues, int[] mutatedValues, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= initialValues.Length)
        {
            return;
        }

        M1StructPropHolder holder = new M1StructPropHolder
        {
            AutoProp = new M1StructCounter { Value = initialValues[tid] }
        };

        int o = tid * 2;
        r[o + 0] = ReadInAfterMutatingPropHolder(holder.AutoProp, ref holder, mutatedValues[tid]);
        r[o + 1] = holder.AutoProp.Value;
    }

    /// <summary>
    /// Reproduces Finding U5-M1-2: User-defined <c>implicit operator</c> on a struct.
    /// <c>StructTranspiler.WriteImplicitConversionOperator</c> ignores the C# conversion operator body and hardcodes
    /// <c>this-&gt;firstMember = v;</c> and <c>return this-&gt;firstMember;</c>.
    /// </summary>
    [GpuKernel]
    public static void StructUserDefinedImplicitConversion(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        M1ScaledInt scaled = inputs[tid];
        int back = scaled;

        int o = tid * 2;
        r[o + 0] = scaled.Value;
        r[o + 1] = back;
    }
}
