#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct MutGetTicker(int b)
{
    public int Next => b++;
}

public struct MutGetCounter
{
    public int N;

    public int Next
    {
        get
        {
            N++;
            return N;
        }
    }

    public int Doubled => N * 2;

#pragma warning disable CS8656 // The implicit copy of this is the behavior under test
    public readonly int PeekNext => Next;
#pragma warning restore CS8656
}

public struct MutGetHolder
{
    public readonly MutGetCounter Ro;
    public MutGetCounter Rw;
    public int Seen;

    public MutGetHolder(int n)
    {
        Ro = new MutGetCounter { N = n };
        Seen = Ro.Next;
        Rw = new MutGetCounter { N = n };
    }
}

public struct MutGetGuard : IDisposable
{
    public int[] Log;
    public int Slot;
    public int State;

    public int Step
    {
        get
        {
            State += 100;
            return State;
        }
    }

    public void Dispose()
    {
        Log[Slot] = State;
    }
}

/// <summary>
/// Differential kernels for property getters that change their struct (#610): on a variable (a <c>using</c> or
/// <c>foreach</c> variable included) they change it, on a readonly variable (an <c>in</c> parameter, a
/// <c>readonly</c> field, <c>this</c> in a <c>readonly</c> member) C# calls them on a copy.
/// </summary>
public static partial class StructMutatingGetterDifferentialKernels
{
    public static readonly string[] MutatingGetterColumns =
    [
        "ticker_first",
        "ticker_second",
        "counter_two_reads",
        "in_param_reads",
        "after_in_param",
        "ref_param_reads",
        "after_ref_param",
        "readonly_member_peek",
        "after_peek",
        "readonly_field_read",
        "readonly_field_after",
        "mutable_field_read",
        "mutable_field_after",
        "constructor_read",
        "using_dispose_log",
        "using_read",
        "foreach_reads",
        "after_foreach",
        "const_getter",
    ];

    [GpuDeviceFunction]
    public static int ReadTwiceThroughIn(in MutGetCounter c) => c.Next + c.Next;

    [GpuDeviceFunction]
    public static int ReadTwiceThroughRef(ref MutGetCounter c) => c.Next + c.Next;

    [GpuKernel]
    public static void MutatingGetterKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int x = inputs[tid] & 0xFFF;
        int o = tid * 19;

        var t = new MutGetTicker(x);
        r[o + 0] = t.Next;
        r[o + 1] = t.Next;

        var c = new MutGetCounter { N = x };
        r[o + 2] = c.Next + (c.Next * 3);
        r[o + 3] = ReadTwiceThroughIn(in c);
        r[o + 4] = c.N;
        r[o + 5] = ReadTwiceThroughRef(ref c);
        r[o + 6] = c.N;
        r[o + 7] = c.PeekNext;
        r[o + 8] = c.N;

        var h = new MutGetHolder(x);
        r[o + 9] = h.Ro.Next;
        r[o + 10] = h.Ro.N;
        r[o + 11] = h.Rw.Next;
        r[o + 12] = h.Rw.N;
        r[o + 13] = h.Seen;

        using (var g = new MutGetGuard { Log = r, Slot = o + 14, State = x })
        {
            r[o + 15] = g.Step;
        }

        MutGetCounter[] counters = new MutGetCounter[2];
        counters[0].N = x;
        counters[1].N = x + 7;
        int sum = 0;
        foreach (var e in counters)
        {
            sum += e.Next + (e.Next * 5);
        }

        r[o + 16] = sum;
        r[o + 17] = counters[0].N + counters[1].N;
        r[o + 18] = c.Doubled;
    }
}
