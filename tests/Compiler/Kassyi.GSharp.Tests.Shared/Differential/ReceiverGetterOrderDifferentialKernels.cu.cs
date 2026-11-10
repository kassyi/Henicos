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
}

/// <summary>
/// Differential kernels for computed struct properties read as the receiver of a member or an element, through the
/// implicit <c>this</c>, or as the target of a compound assignment, next to an operand their getter or setter
/// interferes with: C# calls the accessor where the operand is written.
/// </summary>
public static partial class ReceiverGetterOrderDifferentialKernels
{
    public static readonly string[] Columns =
    [
        "pair_count_next_value",
        "pair_next_value_count",
        "pair_count_next_int_control",
        "pair_current_value_bump",
        "pair_bump_current_value",
        "pair_element_touch_value",
        "pair_touch_value_element",
        "binary_count_next_value",
        "implicit_this_read_then_next",
        "implicit_this_next_then_read",
        "implicit_this_read_then_next_value",
        "implicit_this_binary_read_then_next",
        "pair_count_parenthesized_next_value",
        "pair_element_compound_mark",
        "pair_element_increment_mark",
        "pair_setter_bump_then_stored",
    ];

    [GpuDeviceFunction]
    public static int Pair(int a, int b) => a * 1000 + b;

    [GpuDeviceFunction]
    public static int BumpAt(int[] data, int index)
    {
        data[index] += 100;
        return 7;
    }

    [GpuKernel]
    public static void ReceiverGetterOrderKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int o = t * 16;
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
        r[o + 8] = h.ReadThenNext;
        h.Count = 0;
        r[o + 9] = h.NextThenRead;
        h.Count = 0;
        r[o + 10] = h.ReadThenNextValue;
        h.Count = 0;
        r[o + 11] = h.ReadThenNextBinary;
        h.Count = 0;
        r[o + 12] = Pair(h.Count, (h.Next).Value);

        data[t] = 0;
        v.Stored = 5;
        r[o + 13] = Pair(data[t], v.Mark += 1);
        data[t] = 0;
        v.Stored = 5;
        r[o + 14] = Pair(data[t], v.Mark++);
        data[t] = 0;
        r[o + 15] = Pair(v.Load = 1, BumpAt(data, t)) + v.Stored;
    }
}
