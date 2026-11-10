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

/// <summary>A struct that the launcher transfers as one block, with a property that uses the <c>field</c> keyword.</summary>
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
/// Properties whose accessors use the <c>field</c> keyword: the device stores the backing field and runs the accessors
/// as C# does, and the launcher transfers the stored value.
/// </summary>
public static partial class FieldKeywordPropertyDifferentialKernels
{
    public static readonly string[] AccessorColumns =
    [
        "setter_only",
        "getter_only",
        "both_compound",
        "block_bodies",
        "prefix_increment",
        "postfix_increment",
        "init_object",
        "initializer_and_constructor",
        "mutating_getter",
        "mutating_getter_in_parameter",
        "computed_reads_field_backed",
        "partial_like_auto",
        "auto",
        "partial_with_logic",
        "library_scaled",
    ];

    [GpuDeviceFunction]
    public static int ReadTwice(in DiffFieldKeywordCounting counting) => counting.X + counting.X;

    [GpuKernel]
    public static void FieldKeywordPropertyAccessors(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 15;

        var setterOnly = new DiffFieldKeywordSetterOnly();
        setterOnly.X = v;
        r[o + 0] = setterOnly.X;

        var getterOnly = new DiffFieldKeywordGetterOnly();
        getterOnly.X = v;
        r[o + 1] = getterOnly.X;

        var both = new DiffFieldKeywordBoth();
        both.X = v;
        both.X += 2;
        r[o + 2] = both.X;

        var blocks = new DiffFieldKeywordBlockBodies();
        blocks.X = v;
        int first = blocks.X;
        blocks.X = -v;
        r[o + 3] = first * 1000 + blocks.X;

        var prefix = new DiffFieldKeywordBoth();
        prefix.X = v;
        int prefixed = ++prefix.X;
        r[o + 4] = prefixed + prefix.X;

        var postfix = new DiffFieldKeywordBoth();
        postfix.X = v;
        int old = postfix.X++;
        r[o + 5] = old + postfix.X;

        var initialized = new DiffFieldKeywordInitOnly { X = v };
        r[o + 6] = initialized.X;

        var constructed = new DiffFieldKeywordInitialized(v);
        r[o + 7] = constructed.X * 1000 + constructed.R;

        var counting = new DiffFieldKeywordCounting();
        counting.X = v;
        int a = counting.X;
        int b = counting.X;
        r[o + 8] = a + b * 3;

        int twice = ReadTwice(in counting);
        r[o + 9] = twice * 3 + counting.X;

        var sum = new DiffFieldKeywordSum { A = v };
        sum.X = v;
        r[o + 10] = sum.Sum;

        var partial = new DiffFieldKeywordPartial();
        partial.X = v;
        partial.X += 3;
        r[o + 11] = partial.X;

        var plain = new DiffFieldKeywordAuto();
        plain.X = v;
        plain.X += 3;
        r[o + 12] = plain.X;

        partial.Y = v;
        r[o + 13] = partial.Y;

        var scaled = new LibScaled();
        scaled.X = v;
        scaled.Y = v;
        r[o + 14] = scaled.X * 3 + scaled.Y;
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
