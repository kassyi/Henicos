#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct RecvGetInner
{
    public int Value;
}

public struct RecvGetCounter
{
    public int Count;

    // Changes its struct and returns a struct: read as the receiver of .Value
    public RecvGetInner Next
    {
        get
        {
            Count += 1;
            return new RecvGetInner { Value = Count };
        }
    }

    public int NextInt
    {
        get
        {
            Count += 1;
            return Count;
        }
    }

    // Count, then a getter that changes Count through the implicit this
    public int ReadThenNext => ReceiverGetterOrderDifferentialKernels.Pair(Count, NextInt);

    public int NextThenRead => ReceiverGetterOrderDifferentialKernels.Pair(NextInt, Count);

    public int ReadThenNextValue => ReceiverGetterOrderDifferentialKernels.Pair(Count, Next.Value);

    public int ReadThenNextBinary => (int)((uint)Count * 1000u + (uint)NextInt);
}

public struct RecvGetView
{
    public int[] Data;
    public int Index;
    public int Stored;

    // Reads memory
    public RecvGetInner Current => new RecvGetInner { Value = Data[Index] };

    // Writes memory
    public RecvGetInner Touch
    {
        get
        {
            Data[Index] += 100;
            return new RecvGetInner { Value = 1 };
        }
    }

    // The getter writes memory, the setter only the struct
    public int Mark
    {
        get
        {
            Data[Index] += 100;
            return Stored;
        }
        set => Stored = value;
    }

    // The setter reads memory
    public int Load
    {
        readonly get => Stored;
        set => Stored = value + Data[Index];
    }

    // The setter writes memory: called by an object initializer or a with expression
    public int Poke
    {
        readonly get => Stored;
        set
        {
            Data[Index] += 100;
            Stored = value;
        }
    }
}

/// <summary>
/// Differential kernels for computed struct properties next to an operand their getter or setter interferes with: C#
/// calls the accessor where the operand is written. One kernel per form: the property read as the receiver of a
/// member, read through the implicit <c>this</c>, or assigned (compound assignment, increment, assignment, object
/// initializer, <c>with</c>).
/// </summary>
public static partial class ReceiverGetterOrderDifferentialKernels
{
    public static readonly string[] ReceiverColumns =
    [
        "pair_count_next_value",
        "pair_next_value_count",
        "pair_count_next_int_control",
        "pair_current_value_bump",
        "pair_bump_current_value",
        "pair_element_touch_value",
        "pair_touch_value_element",
        "binary_count_next_value",
        "pair_count_parenthesized_next_value",
    ];

    public static readonly string[] ImplicitThisColumns =
    [
        "read_then_next",
        "next_then_read",
        "read_then_next_value",
        "binary_read_then_next",
    ];

    public static readonly string[] AccessorColumns =
    [
        "pair_element_compound_mark",
        "pair_element_increment_mark",
        "pair_setter_bump_then_stored",
        "pair_element_initializer_poke",
        "pair_element_with_poke",
    ];

    [GpuDeviceFunction]
    public static int Pair(int a, int b) => a * 1000 + b;

    [GpuDeviceFunction]
    public static int BumpAt(int[] data, int index)
    {
        data[index] += 100;
        return 7;
    }

    // A getter that changes its struct, reads memory or writes memory, read as the receiver of .Value
    [GpuKernel]
    public static void ReceiverGetterKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int o = t * 9;
        RecvGetCounter h = default;
        r[o + 0] = Pair(h.Count, h.Next.Value);
        h.Count = 0;
        r[o + 1] = Pair(h.Next.Value, h.Count);
        h.Count = 0;
        r[o + 2] = Pair(h.Count, h.NextInt);

        var v = new RecvGetView { Data = data, Index = t };
        data[t] = 0;
        r[o + 3] = Pair(v.Current.Value, BumpAt(data, t));
        data[t] = 0;
        r[o + 4] = Pair(BumpAt(data, t), v.Current.Value);
        data[t] = 0;
        r[o + 5] = Pair(data[t], v.Touch.Value);
        data[t] = 0;
        r[o + 6] = Pair(v.Touch.Value, data[t]);

        h.Count = 0;
        r[o + 7] = (int)((uint)h.Count * 1000u + (uint)h.Next.Value);
        h.Count = 0;
        r[o + 8] = Pair(h.Count, (h.Next).Value);
    }

    // A getter that changes this, read by its bare name inside the struct
    [GpuKernel]
    public static void ImplicitThisGetterKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int o = t * 4;
        RecvGetCounter h = default;
        r[o + 0] = h.ReadThenNext;
        h.Count = 0;
        r[o + 1] = h.NextThenRead;
        h.Count = 0;
        r[o + 2] = h.ReadThenNextValue;
        h.Count = 0;
        r[o + 3] = h.ReadThenNextBinary;
    }

    // The accessors an assignment calls: the getter of a compound assignment or an increment, the setter of an
    // assignment, an object initializer or a with expression
    [GpuKernel]
    public static void AccessorCallKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int o = t * 5;
        var v = new RecvGetView { Data = data, Index = t };
        data[t] = 0;
        v.Stored = 5;
        r[o + 0] = Pair(data[t], v.Mark += 1);
        data[t] = 0;
        v.Stored = 5;
        r[o + 1] = Pair(data[t], v.Mark++);
        data[t] = 0;
        r[o + 2] = Pair(v.Load = 1, BumpAt(data, t)) + v.Stored;
        data[t] = 0;
        r[o + 3] = Pair(data[t], new RecvGetView { Data = data, Index = t, Poke = 1 }.Stored);
        data[t] = 0;
        r[o + 4] = Pair(data[t], (v with { Poke = 1 }).Stored);
    }
}
