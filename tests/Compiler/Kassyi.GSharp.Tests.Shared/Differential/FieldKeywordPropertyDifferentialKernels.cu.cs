#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A setter that uses the <c>field</c> keyword, with an implemented getter.</summary>
public struct DiffFieldKeywordSetterOnly
{
    public int X { get; set => field = value * 2; }
}

/// <summary>A getter that uses the <c>field</c> keyword, with an implemented setter.</summary>
public struct DiffFieldKeywordGetterOnly
{
    public int X { get => field + 100; set; }
}

/// <summary>Both accessors use the <c>field</c> keyword.</summary>
public struct DiffFieldKeywordBoth
{
    public int X { get => field * 10; set => field = value + 1; }
}

/// <summary>Block bodies that use the <c>field</c> keyword.</summary>
public struct DiffFieldKeywordBlockBodies
{
    public int X
    {
        get
        {
            int t = field;
            return t - 1;
        }
        set
        {
            if (value < 0)
            {
                field = 0;
            }
            else
            {
                field = value;
            }
        }
    }
}

/// <summary>An <c>init</c> accessor that uses the <c>field</c> keyword.</summary>
public struct DiffFieldKeywordInitOnly
{
    public int X { get; init => field = value * 3; }
}

/// <summary>
/// An initializer, which writes the backing field without the setter, and a property without a setter that the
/// constructor assigns, which writes its backing field too.
/// </summary>
public struct DiffFieldKeywordInitialized
{
    public DiffFieldKeywordInitialized(int v)
    {
        R = v;
    }

    public int X { get => field + 1; set => field = value * 2; } = 5;

    public int R { get => field * 3; }
}

/// <summary>A getter that writes its backing field, so C# calls it on a copy of a readonly receiver.</summary>
public struct DiffFieldKeywordCounting
{
    public int X
    {
        get
        {
            field++;
            return field;
        }
        set => field = value;
    }
}

/// <summary>A computed property that reads a property with the <c>field</c> keyword.</summary>
public struct DiffFieldKeywordSum
{
    public int A;

    public int X { get => field - 1; set => field = value * 4; }

    public int Sum => A + X;
}

/// <summary>
/// Partial properties, whose implementing part always has bodies: <c>X</c> behaves like an auto-property, <c>Y</c>
/// has logic of its own.
/// </summary>
public partial struct DiffFieldKeywordPartial
{
    public partial int X { get; set; }

    public partial int Y { get; set; }
}

public partial struct DiffFieldKeywordPartial
{
    public partial int X { get => field; set => field = value; }

    public partial int Y { get => field - 1; set => field = value * 4; }
}

/// <summary>The auto-property that <see cref="DiffFieldKeywordPartial.X"/> behaves like.</summary>
public struct DiffFieldKeywordAuto
{
    public int X { get; set; }
}

/// <summary>
/// A struct that the launcher transfers as one block, with a property that uses the <c>field</c> keyword.
/// </summary>
public struct DiffFieldKeywordPacked
{
    public byte A;

    public long P { get => field + 1; set => field = value * 2; }

    public int B;

    public short Q { get; set; }
}

/// <summary>
/// A struct that the launcher converts member by member (it has an array field), with a property that uses the
/// <c>field</c> keyword: the conversion copies the stored value and never runs the accessors.
/// </summary>
public struct DiffFieldKeywordWithArray
{
    public int[] Data;

    public int P { get => field + 1; set => field = value * 2; }
}

/// <summary>
/// Properties without a setter whose getters use the <c>field</c> keyword, which a constructor steps: C# reads the
/// getter and writes the backing field.
/// </summary>
public struct DiffFieldKeywordConstructorStep
{
    public int Last;

    public DiffFieldKeywordConstructorStep(int v)
    {
        R = v;
        R += 2;
        R++;
        S = v;
        Last = ++S;
        this.S -= 1;
    }

    public int R { get => field * 3; }

    public int S { get => field + 1; }
}

/// <summary>
/// A constructor that deconstructs into properties without a setter, which writes their backing fields.
/// </summary>
public struct DiffFieldKeywordDeconstructed
{
    public DiffFieldKeywordDeconstructed(int a, int b)
    {
        (R, S) = (a, b);
    }

    public int R { get => field * 3; }

    public int S { get => field + 1; }
}

/// <summary>
/// Two properties with the <c>field</c> keyword, for <c>with</c> expressions that set one and copy the other.
/// </summary>
public struct DiffFieldKeywordPair
{
    public int X { get; set => field = value * 2; }

    public int Y { get => field + 1; set; }
}

/// <summary>
/// Properties whose accessors use the <c>field</c> keyword: the device stores the backing field and runs the accessors
/// as C# does, and the launcher transfers the stored value. One kernel per form.
/// </summary>
public static partial class FieldKeywordPropertyDifferentialKernels
{
    public static readonly string[] SetterOnlyColumns = ["read"];

    public static readonly string[] GetterOnlyColumns = ["read"];

    public static readonly string[] CompoundColumns = ["after_compound"];

    public static readonly string[] BlockBodiesColumns = ["positive", "negative"];

    public static readonly string[] StepColumns = ["prefix_value", "after_prefix", "postfix_value", "after_postfix"];

    public static readonly string[] InitColumns = ["read"];

    public static readonly string[] WithColumns = ["init_body", "original", "setter_body", "copied_stored_value"];

    public static readonly string[] ConstructorColumns = ["initializer", "setterless"];

    public static readonly string[] ConstructorStepColumns =
        ["compound_and_postfix", "prefix_and_this", "prefix_value"];

    public static readonly string[] MutatingGetterColumns = ["first_read", "second_read", "through_in", "after_in"];

    public static readonly string[] ComputedReaderColumns = ["sum"];

    public static readonly string[] PartialColumns = ["partial_like_auto", "auto", "partial_with_logic"];

    public static readonly string[] DeconstructionColumns = ["setter", "other", "setterless_r", "setterless_s"];

    public static readonly string[] LibraryColumns = ["field_backed", "auto"];

    public static readonly string[] LibraryMacroColumns = ["auto_property_target"];

    [GpuDeviceFunction]
    public static int ReadTwice(in DiffFieldKeywordCounting counting) => counting.X + counting.X;

    [GpuKernel]
    public static void FieldKeywordSetterOnly(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        var s = new DiffFieldKeywordSetterOnly();
        s.X = inputs[tid] % 1000;
        r[tid] = s.X;
    }

    [GpuKernel]
    public static void FieldKeywordGetterOnly(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        var s = new DiffFieldKeywordGetterOnly();
        s.X = inputs[tid] % 1000;
        r[tid] = s.X;
    }

    [GpuKernel]
    public static void FieldKeywordCompound(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        var s = new DiffFieldKeywordBoth();
        s.X = inputs[tid] % 1000;
        s.X += 2;
        r[tid] = s.X;
    }

    [GpuKernel]
    public static void FieldKeywordBlockBodies(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 2;
        var s = new DiffFieldKeywordBlockBodies();
        s.X = v;
        r[o] = s.X;
        s.X = -v;
        r[o + 1] = s.X;
    }

    [GpuKernel]
    public static void FieldKeywordSteps(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 4;
        var prefix = new DiffFieldKeywordBoth();
        prefix.X = v;
        r[o] = ++prefix.X;
        r[o + 1] = prefix.X;
        var postfix = new DiffFieldKeywordBoth();
        postfix.X = v;
        r[o + 2] = postfix.X++;
        r[o + 3] = postfix.X;
    }

    [GpuKernel]
    public static void FieldKeywordInit(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        var s = new DiffFieldKeywordInitOnly { X = inputs[tid] % 1000 };
        r[tid] = s.X;
    }

    [GpuKernel]
    public static void FieldKeywordWith(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 4;
        var initialized = new DiffFieldKeywordInitOnly { X = v };
        var copied = initialized with { X = v + 1 };
        r[o] = copied.X;
        r[o + 1] = initialized.X;
        var pair = new DiffFieldKeywordPair { X = v, Y = 5 };
        var setter = pair with { X = 7 };
        var other = pair with { Y = 9 };
        r[o + 2] = setter.X + setter.Y;
        r[o + 3] = other.X * 100 + other.Y;
    }

    [GpuKernel]
    public static void FieldKeywordConstructor(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int o = tid * 2;
        var s = new DiffFieldKeywordInitialized(inputs[tid] % 1000);
        r[o] = s.X;
        r[o + 1] = s.R;
    }

    [GpuKernel]
    public static void FieldKeywordConstructorStep(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int o = tid * 3;
        var s = new DiffFieldKeywordConstructorStep(inputs[tid] % 1000);
        r[o] = s.R;
        r[o + 1] = s.S;
        r[o + 2] = s.Last;
    }

    [GpuKernel]
    public static void FieldKeywordMutatingGetter(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int o = tid * 4;
        var counting = new DiffFieldKeywordCounting();
        counting.X = inputs[tid] % 1000;
        r[o] = counting.X;
        r[o + 1] = counting.X;
        r[o + 2] = ReadTwice(in counting);
        r[o + 3] = counting.X;
    }

    [GpuKernel]
    public static void FieldKeywordComputedReader(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        var sum = new DiffFieldKeywordSum { A = v };
        sum.X = v;
        r[tid] = sum.Sum;
    }

    [GpuKernel]
    public static void FieldKeywordPartial(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 3;
        var partial = new DiffFieldKeywordPartial();
        partial.X = v;
        partial.X += 3;
        r[o] = partial.X;
        var plain = new DiffFieldKeywordAuto();
        plain.X = v;
        plain.X += 3;
        r[o + 1] = plain.X;
        partial.Y = v;
        r[o + 2] = partial.Y;
    }

    [GpuKernel]
    public static void FieldKeywordDeconstruction(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 4;
        var target = new DiffFieldKeywordBoth();
        int other;
        (target.X, other) = (v, 1);
        r[o] = target.X;
        r[o + 1] = other;
        var deconstructed = new DiffFieldKeywordDeconstructed(v, v + 3);
        r[o + 2] = deconstructed.R;
        r[o + 3] = deconstructed.S;
    }

    [GpuKernel]
    public static void FieldKeywordLibrary(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 2;
        var scaled = new LibScaled();
        scaled.X = v;
        scaled.Y = v;
        r[o] = scaled.X;
        r[o + 1] = scaled.Y;
    }

    [GpuKernel]
    public static void FieldKeywordLibraryMacroTarget(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        var holder = new LibMacroHolder();
        var a = new LibMacroValue { V = inputs[tid] % 1000 };
        holder.P = a + a;
        r[tid] = holder.P.V;
    }

    [GpuKernel]
    public static void FieldKeywordPropertyBlockCopy(DiffFieldKeywordPacked[] items, long[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= items.Length)
        {
            return;
        }

        r[tid] = items[tid].P;
        items[tid].P = items[tid].P + 3;
        items[tid].B = items[tid].B + 1;
        items[tid].Q = (short)(items[tid].Q + 1);
    }

    [GpuKernel]
    public static void FieldKeywordPropertyMemberwise(DiffFieldKeywordWithArray[] items, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= items.Length)
        {
            return;
        }

        r[tid] = items[tid].P + items[tid].Data[0];
        items[tid].P = 1;
        items[tid].P = items[tid].P + items[tid].Data[0];
    }
}
