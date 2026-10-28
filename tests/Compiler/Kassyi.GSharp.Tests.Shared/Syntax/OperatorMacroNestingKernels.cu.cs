// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A value that an operator macro computes into a <c>ref</c> result, so a temporary can hold it.</summary>
public struct NestValue
{
    public int Value;

    public NestValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(OperatorMacroNestingKernels), nameof(OperatorMacroNestingKernels.Combine))]
    public static NestValue operator -(NestValue left, NestValue right) => throw new GpuOnlyException();
}

/// <summary>The context of <see cref="LoggedValue"/>'s operator: its target writes a log that an operand reads.</summary>
public struct NestLog
{
    public int[] Entries;
}

public struct LoggedValue
{
    public int Value;

    public LoggedValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(OperatorMacroNestingKernels), nameof(OperatorMacroNestingKernels.CombineAndLog))]
    public static LoggedValue operator -(LoggedValue left, LoggedValue right) => throw new GpuOnlyException();
}

/// <summary>
/// An operator macro used as an operand of another one (<c>c = c - (a - b)</c>): the inner operator is computed into a
/// temporary first, in the C# order of evaluation. <see cref="OperatorMacroNestingTests"/> checks the values.
/// </summary>
public static partial class OperatorMacroNestingKernels
{
    // Not symmetric in left and right, so swapped operands show
    [GpuDeviceFunction]
    public static void Combine(ref NestValue result, NestValue left, NestValue right)
    {
        result.Value = left.Value * 10 + right.Value;
    }

    // Writes the log after reading the operands
    [GpuDeviceFunction]
    public static void CombineAndLog(ref NestLog log, ref LoggedValue result, LoggedValue left, LoggedValue right)
    {
        result.Value = left.Value * 10 + right.Value;
        log.Entries[0] = 100;
    }

    [GpuKernel]
    public static void NestedKernel(int[] values, int[] result)
    {
        NestValue a = new NestValue(values[0]);
        NestValue b = new NestValue(values[1]);
        NestValue c = new NestValue(values[2]);
        c = c - (a - b);

        // On the CPU dialects the barrier turns the kernel into a state machine: the expansion reads the lowered tree
        Gpu.SyncThreads();
        result[0] = c.Value;

        // Both operands nested, and nested twice
        NestValue d = new NestValue(0);
        d = (a - b) - (b - (a - c));
        result[1] = d.Value;
    }

    // C# reads log[0] for the left operand before the inner operator writes it
    [GpuKernel]
    public static void NestedOrderKernel(int[] entries, int[] result)
    {
        NestLog log = default;
        log.Entries = entries;
        LoggedValue a = new LoggedValue(1);
        LoggedValue b = new LoggedValue(2);
        LoggedValue c = new LoggedValue(0);
        c = new LoggedValue(entries[0]) - (a - b);
        result[0] = c.Value;
    }
}

// Regression cases of the order of evaluation around an inner call: the inner target reads memory that an earlier
// operand writes, or writes a context that an earlier operand reads
public struct RvValue
{
    public int Value;

    public RvValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(OperatorMacroNestingOrderKernels), nameof(OperatorMacroNestingOrderKernels.CombineValues))]
    public static RvValue operator -(RvValue left, RvValue right) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroNestingOrderKernels), nameof(OperatorMacroNestingOrderKernels.CombineRead))]
    public static RvValue operator /(RvValue left, RvSource source) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroNestingOrderKernels), nameof(OperatorMacroNestingOrderKernels.CombineLast))]
    public static RvValue operator &(RvValue left, RvValue right) => throw new GpuOnlyException();
}

public struct RvSource
{
    public int[] Data;
}

public struct RvLast
{
    public RvValue Last;
}

public static partial class OperatorMacroNestingOrderKernels
{
    [GpuDeviceFunction]
    public static void CombineValues(ref RvValue result, RvValue left, RvValue right)
    {
        result.Value = left.Value * 10 + right.Value;
    }

    // Reads memory
    [GpuDeviceFunction]
    public static void CombineRead(ref RvValue result, RvValue left, RvSource source)
    {
        result.Value = left.Value * 10 + source.Data[0];
    }

    // Writes its context
    [GpuDeviceFunction]
    public static void CombineLast(ref RvLast log, ref RvValue result, RvValue left, RvValue right)
    {
        result.Value = left.Value * 10 + right.Value;
        log.Last = new RvValue(9);
    }

    [GpuDeviceFunction]
    public static RvValue WriteFirst(int[] data, int value)
    {
        data[0] = value;
        return new RvValue(value);
    }

    // C# calls WriteFirst before the inner operator reads data[0]
    [GpuKernel]
    public static void InnerReadsMemoryKernel(int[] data, int[] result)
    {
        RvSource source = default;
        source.Data = data;
        RvValue a = new RvValue(1);
        RvValue c = new RvValue(0);
        c = WriteFirst(data, 5) - (a / source);
        result[0] = c.Value;
    }

    // C# reads log.Last before the inner operator writes it
    [GpuKernel]
    public static void InnerWritesContextKernel(int[] result)
    {
        RvLast log = default;
        log.Last = new RvValue(3);
        RvValue a = new RvValue(1);
        RvValue b = new RvValue(2);
        RvValue c = new RvValue(0);
        c = log.Last & (a & b);
        result[0] = c.Value;
        result[1] = log.Last.Value;
    }
}

// Outer operators of other shapes around a nested operator macro: a shift (operands [left, extra]), a unary operator,
// and an identity cast around the inner operator
public struct ShapeValue
{
    public int Value;

    public ShapeValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(OperatorMacroShapeKernels), nameof(OperatorMacroShapeKernels.Combine))]
    public static ShapeValue operator -(ShapeValue left, ShapeValue right) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroShapeKernels), nameof(OperatorMacroShapeKernels.Shift))]
    public static ShapeValue operator <<(ShapeValue value, int shift) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroShapeKernels), nameof(OperatorMacroShapeKernels.Negate))]
    public static ShapeValue operator -(ShapeValue value) => throw new GpuOnlyException();
}

public static partial class OperatorMacroShapeKernels
{
    [GpuDeviceFunction]
    public static void Combine(ref ShapeValue result, ShapeValue left, ShapeValue right)
    {
        result.Value = left.Value * 10 + right.Value;
    }

    [GpuDeviceFunction]
    public static void Shift(ref ShapeValue result, ShapeValue value, int shift)
    {
        result.Value = value.Value * 100 + shift;
    }

    [GpuDeviceFunction]
    public static void Negate(ref ShapeValue result, ShapeValue value)
    {
        result.Value = 7 - value.Value;
    }

    [GpuKernel]
    public static void ShapesKernel(int[] result)
    {
        ShapeValue a = new ShapeValue(1);
        ShapeValue b = new ShapeValue(2);
        ShapeValue c = new ShapeValue(3);
        c = (a - b) << 2;
        result[0] = c.Value;
        c = -(a - b);
        result[1] = c.Value;
        c = new ShapeValue(3);
        c = c - (ShapeValue)(a - b);
        result[2] = c.Value;
    }
}

public static partial class OperatorMacroNestingOrderKernels
{
    // The context is a ref parameter here: what the inner call writes through it is memory
    [GpuDeviceFunction]
    public static RvValue CombineThroughRef(ref RvLast log, RvValue a, RvValue b)
    {
        RvValue c = new RvValue(0);
        c = log.Last & (a & b);
        return c;
    }

    [GpuKernel]
    public static void InnerWritesRefContextKernel(int[] result)
    {
        RvLast log = default;
        log.Last = new RvValue(3);
        RvValue c = CombineThroughRef(ref log, new RvValue(1), new RvValue(2));
        result[0] = c.Value;
        result[1] = log.Last.Value;
    }
}
