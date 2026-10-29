// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A value whose operator macros pass an operand by <c>in</c>.</summary>
public struct InValue
{
    public int V;

    public InValue(int v) => V = v;

    [GpuOperatorMacro(typeof(ArgumentOps), nameof(ArgumentOps.AddIn))]
    public static InValue operator +(InValue left, InValue right) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(ArgumentOps), nameof(ArgumentOps.CombineInAndCount))]
    public static InValue operator &(InValue left, InValue right) => throw new GpuOnlyException();
}

/// <summary>The context of <see cref="InValue"/>'s <c>&amp;</c>: its target counts its calls and writes the first value.</summary>
public struct InCounter
{
    public int Calls;
    public InValue[] Values;
}

/// <summary>A value whose operator macro writes its result through a <c>ref</c> parameter and counts its calls.</summary>
public struct SpillValue
{
    public int V;

    public SpillValue(int v) => V = v;

    [GpuOperatorMacro(typeof(ArgumentOps), nameof(ArgumentOps.Combine))]
    public static SpillValue operator -(SpillValue left, SpillValue right) => throw new GpuOnlyException();
}

public struct SpillCtx
{
    public int Calls;
}

/// <summary>A value whose operator macro's target takes the result as <c>out</c>.</summary>
public struct OutValue
{
    public int V;

    public OutValue(int v) => V = v;

    [GpuOperatorMacro(typeof(ArgumentOps), nameof(ArgumentOps.AddOut))]
    public static OutValue operator +(OutValue left, OutValue right) => throw new GpuOnlyException();
}

public static partial class ArgumentOps
{
    [GpuDeviceFunction]
    public static void AddIn(ref InValue result, in InValue left, InValue right)
    {
        result.V = left.V * 10 + right.V;
    }

    // Reads left, then writes the first value
    [GpuDeviceFunction]
    public static void CombineInAndCount(ref InCounter ctx, ref InValue result, in InValue left, InValue right)
    {
        result.V = left.V * 10 + right.V;
        ctx.Calls = ctx.Calls + 1;
        ctx.Values[0] = new InValue(7);
    }

    [GpuDeviceFunction]
    public static void Combine(ref SpillCtx ctx, ref SpillValue result, SpillValue left, SpillValue right)
    {
        result.V = left.V * 10 + right.V;
        ctx.Calls = ctx.Calls + 1;
    }

    [GpuDeviceFunction]
    public static void AddOut(out OutValue result, OutValue left, OutValue right)
    {
        result = new OutValue(left.V + right.V);
    }
}

/// <summary>
/// The ways an operator macro passes its operands to the target (#711): by <c>in</c>, a variable passed by <c>in</c>
/// before an inner operator macro that writes it, a result passed by <c>ref</c> before an inner operator macro that
/// changes its index, and an <c>out</c> result. <see cref="OperatorMacroArgumentTests"/> checks the values on the
/// native target, <c>OperatorMacroArgumentManagedTests</c> on the managed fallback.
/// </summary>
public static partial class OperatorMacroArgumentKernels
{
    [GpuKernel]
    public static void InArgumentKernel(int[] result)
    {
        InValue a = new InValue(1);
        InValue b = new InValue(2);
        InValue c = new InValue(0);
        c = a + b;
        result[0] = c.V;
    }

    // values[ctx.Calls] is evaluated first (the element at 0), before a & b increments ctx.Calls, as C# evaluates the
    // operands; it is passed by 'in', so the target reads the element itself, after a & b wrote 7 into it
    [GpuKernel]
    public static void InSpillKernel(InValue[] values, int[] result)
    {
        InCounter ctx = default;
        ctx.Values = values;
        InValue a = new InValue(1);
        InValue b = new InValue(2);
        InValue c = new InValue(0);
        c = values[ctx.Calls] & (a & b);
        result[0] = c.V;
        result[1] = ctx.Calls;
    }

    // The element is chosen before b - c increments ctx.Calls, as C# evaluates the target first
    [GpuKernel]
    public static void RefSpillKernel(SpillValue[] values, int[] calls)
    {
        SpillCtx ctx = default;
        SpillValue a = new SpillValue(1);
        SpillValue b = new SpillValue(2);
        SpillValue c = new SpillValue(3);
        values[ctx.Calls] = a - (b - c);
        calls[0] = ctx.Calls;
    }

    // The target assigns the out result: the local needs no value before
    [GpuKernel]
    public static void OutResultKernel(int[] result)
    {
        OutValue a = new OutValue(4);
        OutValue c;
        c = a + a;
        result[0] = c.V;
        OutValue d;
        SumInto(out d, new OutValue(5));
        result[1] = d.V;
    }

    [GpuDeviceFunction]
    public static void SumInto(out OutValue sum, OutValue value)
    {
        sum = value + value;
    }
}
