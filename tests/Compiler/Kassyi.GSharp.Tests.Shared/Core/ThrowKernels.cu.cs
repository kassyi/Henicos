// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>Kernels that throw, for <see cref="ThrowTests"/>: <c>outs[1]</c> is written after the throwing statement.</summary>
public static partial class ThrowKernels
{
    // A device function whose throw is an expression
    [GpuDeviceFunction]
    public static int Checked(int v) => v >= 0 ? v : throw new ArgumentOutOfRangeException(nameof(v));

    [GpuKernel]
    public static void StatementKernel(int[] outs, int mode)
    {
        outs[0] = 1;
        if (mode == 1)
        {
            throw new InvalidOperationException("stop here");
        }

        if (mode == 2)
        {
            throw new FormatException("bad format");
        }

        if (mode == 3)
        {
            throw new ArgumentException();
        }

        if (mode == 4)
        {
            throw new NotImplementedException();
        }

        outs[1] = 2;
    }

    [GpuKernel]
    public static void ExpressionKernel(int[] outs, int v)
    {
        outs[0] = Checked(v);
        outs[1] = 9;
    }

    [GpuKernel]
    public static void CoalesceKernel(int[] outs, int v)
    {
        int? n = v > 0 ? v : null;
        outs[0] = n ?? throw new InvalidOperationException("no value");
        outs[1] = 9;
    }

    // The arguments of the exception are evaluated before it is thrown, as C# does
    [GpuDeviceFunction]
    public static int Mark(int[] outs, int slot)
    {
        outs[slot] = 7;
        return slot;
    }

    [GpuKernel]
    public static void ArgumentsKernel(int[] outs, int mode)
    {
        if (mode == 1)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), Mark(outs, 2), "mode is out of range");
        }

        outs[1] = 9;
    }
}
