using System;
using Kassyi.GSharp.Core;

namespace OperatorMacroPlanCases;

public struct Ctx
{
    public int State;
}

public struct Value
{
    public int X;

    // ByValue.Add (value operands) and ByIn.Add ('in' operands) apply equally well: the value parameters win
    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Add))]
    public static Value operator +(Value left, Value right) => throw new InvalidOperationException();

    // Sub takes a context first, and its right operand by 'in'
    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Sub))]
    public static Value operator -(Value left, Value right) => throw new InvalidOperationException();

    // The int operand converts to both long and int: the exact int wins
    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Mul))]
    public static Value operator *(Value left, int right) => throw new InvalidOperationException();

    // The byte operand converts to both int and uint: the signed int is the better conversion target
    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Scale))]
    public static Value operator /(Value left, byte right) => throw new InvalidOperationException();

    // Only a long overload: the short operand converts to it
    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Widen))]
    public static Value operator %(Value left, short right) => throw new InvalidOperationException();

    // A shift passes its count as the extra operand
    [GpuOperatorMacro(typeof(Ops), nameof(Ops.Shift))]
    public static Value operator <<(Value value, int count) => throw new InvalidOperationException();
}

public static class Ops
{
    [GpuDeviceFunction]
    public static void Add(ref Value result, in Value left, in Value right)
    {
    }

    [GpuDeviceFunction]
    public static void Add(ref Value result, Value left, Value right)
    {
    }

    [GpuDeviceFunction]
    public static void Sub(Ctx ctx, ref Value result, Value left, in Value right)
    {
    }

    [GpuDeviceFunction]
    public static void Mul(ref Value result, Value left, long right)
    {
    }

    [GpuDeviceFunction]
    public static void Mul(ref Value result, Value left, int right)
    {
    }

    [GpuDeviceFunction]
    public static void Scale(ref Value result, Value left, uint right)
    {
    }

    [GpuDeviceFunction]
    public static void Scale(ref Value result, Value left, int right)
    {
    }

    [GpuDeviceFunction]
    public static void Widen(ref Value result, Value left, long right)
    {
    }

    [GpuDeviceFunction]
    public static void Shift(ref Value result, Value value, int count)
    {
    }

    // Writes its argument, so an operand after it that reads the same variable sees the new value
    [GpuDeviceFunction]
    public static int Take(ref int index)
    {
        index++;
        return index;
    }
}

public static class Kernels
{
    [GpuKernel]
    public static void AddKernel(Value a, Value b)
    {
        Value c = default;
        c = a + b;
    }

    [GpuKernel]
    public static void ContextKernel(Value a, Value b)
    {
        Ctx ctx = default;
        Value c = default;
        c = a - b;
    }

    [GpuKernel]
    public static void ConversionKernel(Value a, short n)
    {
        Value c = default;
        c = a % n;
    }

    [GpuKernel]
    public static void ShiftKernel(Value a, int n)
    {
        Value c = default;
        c = a << n;
    }

    [GpuKernel]
    public static void NestedKernel(Value a, Value b, Value d)
    {
        Value c = default;
        c = (a + b) + d;
    }

    [GpuKernel]
    public static void SpillKernel(Value[] values, int k)
    {
        values[Ops.Take(ref k)] = values[k] * 2;
    }
}
