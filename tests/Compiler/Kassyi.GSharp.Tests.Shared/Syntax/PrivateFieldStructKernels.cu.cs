using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The launcher converts these structs member by member (each holds an array), and every member it copies is private:
// generated code cannot name them, so it reads and writes them through the host mirror of the shadow struct.

/// <summary>A converted struct whose element type of a private struct array is itself converted.</summary>
public struct PfInner
{
    private int[] _values;
    private int _bias;

    public PfInner(int[] values, int bias)
    {
        _values = values;
        _bias = bias;
    }

    public int Bias
    {
        readonly get => _bias;
        set => _bias = value;
    }

    public readonly int[] Values => _values;
}

/// <summary>Private scalar, array, nested struct, struct array and fixed buffer fields.</summary>
public unsafe struct PfHolder
{
    private int _scale;
    private int[] _data;
    private PfInner _inner;
    private PfInner[] _items;
    private fixed int _buffer[4];

    public PfHolder(int scale, int[] data, PfInner inner, PfInner[] items)
    {
        _scale = scale;
        _data = data;
        _inner = inner;
        _items = items;
        for (int i = 0; i < 4; i++)
        {
            _buffer[i] = (i + 1) * 100;
        }
    }

    public int Scale
    {
        readonly get => _scale;
        set => _scale = value;
    }

    public readonly int[] Data => _data;

    public PfInner Inner
    {
        readonly get => _inner;
        set => _inner = value;
    }

    public readonly PfInner[] Items => _items;

    public int First
    {
        get => _buffer[0];
        set => _buffer[0] = value;
    }

    public int Last
    {
        get => _buffer[3];
        set => _buffer[3] = value;
    }
}

public static partial class PrivateFieldStructKernels
{
    [GpuKernel]
    public static void StepPrivateFieldsKernel(ref PfHolder holder)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        var inner = holder.Inner;
        int[] data = holder.Data;
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = data[i] * holder.Scale + inner.Bias;
        }

        inner.Values[0] = holder.First + holder.Last;
        inner.Bias = inner.Bias + 10;
        holder.Inner = inner;
        holder.First = holder.First + 1;
        holder.Last = holder.Last + 1;

        PfInner[] items = holder.Items;
        items[1].Values[0] = items[0].Bias + items[1].Bias;
        items[0].Bias = items[0].Bias + 1;
        holder.Scale = -holder.Scale;
    }
}
