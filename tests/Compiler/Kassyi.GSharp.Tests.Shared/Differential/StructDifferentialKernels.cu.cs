#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffStructCounter
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
        set => this = new DiffStructCounter { Value = value };
    }
}

public struct DiffStructReadonlyHolder
{
    public readonly DiffStructCounter ReadonlyInner;
    public DiffStructCounter MutableInner;

    public DiffStructReadonlyHolder(int roVal, int mutVal)
    {
        ReadonlyInner = new DiffStructCounter { Value = roVal };
        MutableInner = new DiffStructCounter { Value = mutVal };
    }
}

public struct DiffStructPropHolder
{
    public DiffStructCounter AutoProp { get; set; }
    public DiffStructCounter Backing;

    public DiffStructCounter ComputedProp
    {
        get => Backing;
        set => Backing = value;
    }
}

public struct DiffStructWithExplicitParameterlessCtor
{
    public int A = 10;
    public int B { get; set; } = 20;
    public int C;

    public DiffStructWithExplicitParameterlessCtor()
    {
        C = A + B + 5;
    }

    public DiffStructWithExplicitParameterlessCtor(int b)
    {
        B = b;
        C = A + B;
    }

    public DiffStructWithExplicitParameterlessCtor(int a, int b, bool earlyReturn) : this()
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

public struct DiffStructWithFieldInitAndParamCtorOnly
{
    public int A = 15;
    public int B { get; set; } = 25;
    public int C;

    public DiffStructWithFieldInitAndParamCtorOnly(int c)
    {
        C = A + B + c;
    }
}

public record struct DiffRecordPoint(int X, int Y, int Z)
{
    public int Tag { get; set; } = 7;

    public int Sum => X + Y + Z + Tag;
}

public struct DiffScaledInt
{
    public int Value;

    public static implicit operator DiffScaledInt(int v) =>
        new DiffScaledInt { Value = (v * 4) + 3 };

    public static implicit operator int(DiffScaledInt s) =>
        s.Value + 100;
}

/// <summary>
/// Differential kernels (<c>struct</c> value semantics, constructors, <c>in</c> parameters,
/// <c>readonly</c> fields, <c>with</c> expressions, and conversion operators).
/// </summary>
public static partial class StructDifferentialKernels
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
    public static int MutateLocalAndReturn(DiffStructCounter byValue, ref DiffStructCounter byRef, int delta)
    {
        byValue.Addend = delta;
        byRef.Addend = delta * 2;
        return byValue.Value;
    }

    [GpuDeviceFunction]
    public static int ReadInCopyMutatedLocally(in DiffStructCounter inCounter, int delta)
    {
        DiffStructCounter localCopy = inCounter;
        localCopy.Addend = delta;
        return localCopy.Value + inCounter.Value + inCounter.ComputedDoubled;
    }

    [GpuDeviceFunction]
    public static int ReadInAfterMutatingPropHolder(in DiffStructCounter inSnapshot, ref DiffStructPropHolder holder, int newValue)
    {
        holder.AutoProp = new DiffStructCounter { Value = newValue };
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
        DiffStructCounter local = new DiffStructCounter { Value = v };
        DiffStructCounter assignedCopy = local;
        assignedCopy.Addend = d;
        local.Addend = 1;

        int byValResult = MutateLocalAndReturn(local, ref local, d);

        // 2. Setter that assigns `this = ...`
        DiffStructCounter resettable = new DiffStructCounter { Value = v };
        resettable.ResetTo = v + d;

        // 3. `in` struct parameter of a [GpuDeviceFunction] helper: mutating a copy inside the helper
        //    leaves the caller's struct untouched.
        DiffStructCounter callerStruct = new DiffStructCounter { Value = v };
        int inHelperResult = ReadInCopyMutatedLocally(in callerStruct, d);

        // 4. `readonly` struct field vs mutable struct field of an enclosing struct:
        DiffStructReadonlyHolder roHolder = new DiffStructReadonlyHolder(v, v + 1);
        int roRead = ReadInCopyMutatedLocally(in roHolder.ReadonlyInner, d);
        roHolder.MutableInner.Addend = d;

        // 5. Property getter returning a struct: reading computed getter on property return value,
        //    and mutating a copy of the property return value does not mutate the holder.
        DiffStructPropHolder propHolder = new DiffStructPropHolder
        {
            AutoProp = new DiffStructCounter { Value = v },
            Backing = new DiffStructCounter { Value = v + 2 }
        };
        DiffStructCounter fromAutoProp = propHolder.AutoProp;
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
        DiffStructWithExplicitParameterlessCtor sDefault = default;
        DiffStructWithExplicitParameterlessCtor sNew0 = new DiffStructWithExplicitParameterlessCtor();
        DiffStructWithExplicitParameterlessCtor sNew1 = new DiffStructWithExplicitParameterlessCtor(v);
        DiffStructWithExplicitParameterlessCtor sNew2 = new DiffStructWithExplicitParameterlessCtor(v, v + 3, early);
        DiffStructWithExplicitParameterlessCtor sNewInit = new DiffStructWithExplicitParameterlessCtor { B = v + 9 };

        // 2. default(S) vs new S() vs new S(c) when S has field initializers and only a parameterized constructor:
        //    In C#, both default(S) and new S() produce all-zero fields (field initializers do NOT run on implicit new S()).
        DiffStructWithFieldInitAndParamCtorOnly pDefault = default;
        DiffStructWithFieldInitAndParamCtorOnly pImplicitNew = new DiffStructWithFieldInitAndParamCtorOnly();
        DiffStructWithFieldInitAndParamCtorOnly pParamNew = new DiffStructWithFieldInitAndParamCtorOnly(v);

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

        DiffRecordPoint p0 = new DiffRecordPoint(x, y, z);

        // Basic `with` expression: original p0 is unchanged, copy p1 has updated X and Tag.
        DiffRecordPoint p1 = p0 with { X = x + 10, Tag = 99 };

        // Side-effecting initializer expressions and reading another field of the receiver:
        int sideEffect = 1;
        DiffRecordPoint p2 = p1 with
        {
            X = p1.Y + (++sideEffect),
            Y = p1.X + (sideEffect *= 3)
        };

        // Reassigning the receiver source variable inside an initializer expression:
        // The cloned receiver has already been copied before the member initializers run, so Z and Tag
        // come from `moving`'s original snapshot, while Y = moving.Y reads the newly assigned `moving`.
        DiffRecordPoint moving = p0;
        DiffRecordPoint replacement = new DiffRecordPoint(100, 200, 300) { Tag = 400 };
        DiffRecordPoint p3 = moving with
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
    /// Reproduces #999: Passing a struct auto-property (<c>holder.AutoProp</c>) to an <c>in</c> parameter
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

        DiffStructPropHolder holder = new DiffStructPropHolder
        {
            AutoProp = new DiffStructCounter { Value = initialValues[tid] }
        };

        int o = tid * 2;
        r[o + 0] = ReadInAfterMutatingPropHolder(holder.AutoProp, ref holder, mutatedValues[tid]);
        r[o + 1] = holder.AutoProp.Value;
    }

    /// <summary>
    /// Reproduces #963: User-defined <c>implicit operator</c> on a struct.
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

        DiffScaledInt scaled = inputs[tid];
        int back = scaled;

        int o = tid * 2;
        r[o + 0] = scaled.Value;
        r[o + 1] = back;
    }
}
