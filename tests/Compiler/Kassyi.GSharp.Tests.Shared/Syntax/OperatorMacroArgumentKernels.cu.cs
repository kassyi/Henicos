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

/// <summary>A value whose operator macro passes an operand by <c>in</c> and takes block shared memory as its context.</summary>
public struct SharedInValue
{
    public int V;

    public SharedInValue(int v) => V = v;

    [GpuOperatorMacro(typeof(ArgumentOps), nameof(ArgumentOps.CombineInShared))]
    public static SharedInValue operator |(SharedInValue left, SharedInValue right) => throw new GpuOnlyException();
}

/// <summary>A value whose operator macro takes both operands by <c>in</c> and writes the result before it reads them again.</summary>
public struct AliasValue
{
    public int V;

    public AliasValue(int v) => V = v;

    [GpuOperatorMacro(typeof(ArgumentOps), nameof(ArgumentOps.AddAlias))]
    public static AliasValue operator +(AliasValue left, AliasValue right) => throw new GpuOnlyException();
}

/// <summary>A struct whose auto-property is the target or an operand of an operator macro.</summary>
public struct PropHolder
{
    public AliasValue P { get; set; }
}

/// <summary>A struct that holds <see cref="PropHolder"/> as a field and as an auto-property.</summary>
public struct PropOuter
{
    public PropHolder Holder;

    public PropHolder Auto { get; set; }
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

    // Reads left, then writes the first element of the shared memory
    [GpuDeviceFunction]
    public static void CombineInShared(SharedMemory<SharedInValue> shared, ref SharedInValue result, in SharedInValue left,
        SharedInValue right)
    {
        result.V = left.V * 10 + right.V;
        shared[0] = new SharedInValue(7);
    }

    // Writes the result, then reads left again: when the result and left are one variable, left has changed
    [GpuDeviceFunction]
    public static void AddAlias(ref AliasValue result, in AliasValue left, in AliasValue right)
    {
        result.V = left.V + right.V;
        result.V = result.V + left.V * 100;
    }

    [GpuDeviceFunction]
    public static ref InValue At(InValue[] values, int index) => ref values[index];
}

/// <summary>
/// The ways an operator macro passes its operands to the target (#711): by <c>in</c>, a variable passed by <c>in</c>
/// before an inner operator macro that writes it (also an element returned by reference from a call or a shared memory
/// indexer), a result passed by <c>ref</c> before an inner operator macro that changes its index, an <c>out</c> result,
/// and an auto-property as the result and as an operand. <see cref="OperatorMacroArgumentTests"/> checks the values on the
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

    // As InSpillKernel, with the element returned by reference from a call: the target reads it after a & b wrote 7
    [GpuKernel]
    public static void RefCallSpillKernel(InValue[] values, int[] result)
    {
        InCounter ctx = default;
        ctx.Values = values;
        InValue a = new InValue(1);
        InValue b = new InValue(2);
        InValue c = new InValue(0);
        c = ArgumentOps.At(values, 0) & (a & b);
        result[0] = c.V;
        result[1] = ctx.Calls;
    }

    // As InSpillKernel, with a shared memory element (a ref-returning indexer) that a | b writes
    [GpuKernel]
    public static void SharedRefSpillKernel(int[] result)
    {
        SharedMemory<SharedInValue> shared = new SharedMemory<SharedInValue>(2);
        shared[0] = new SharedInValue(3);
        shared[1] = new SharedInValue(5);
        SharedInValue a = new SharedInValue(1);
        SharedInValue b = new SharedInValue(2);
        SharedInValue c = new SharedInValue(0);
        c = shared[0] | (a | b);
        result[0] = c.V;
        result[1] = shared[0].V;
        result[2] = shared[1].V;
    }

    // An auto-property as the target and as an in operand: the device stores it as a field, so the target writes into
    // it, and reads the same storage through left when the two are one property
    [GpuKernel]
    public static void PropertyTargetKernel(int[] result)
    {
        PropHolder h = default;
        AliasValue x = new AliasValue(1);
        AliasValue y = new AliasValue(10);
        h.P = x + y;
        result[0] = h.P.V;
        h.P = h.P + y;
        result[1] = h.P.V;
        PropOuter o = default;
        o.Holder.P = x + y;
        o.Auto = o.Holder;
        h.P = o.Auto.P + y;
        result[2] = o.Holder.P.V;
        result[3] = h.P.V;
        result[4] = AddThroughProperty(x, y);
    }

    [GpuDeviceFunction]
    public static int AddThroughProperty(AliasValue x, AliasValue y)
    {
        PropHolder h = default;
        h.P = x + y;
        return h.P.V;
    }
}
