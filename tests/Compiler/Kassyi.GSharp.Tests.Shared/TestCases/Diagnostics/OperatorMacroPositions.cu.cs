using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class PositionOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
    }

    [GpuDeviceFunction]
    public static void Next(uint[] result, uint[] operand)
    {
    }
}

// The target writes the result into the buffer of the assignment target, so an inner operator has nowhere to go
public readonly ref struct BufferRef
{
    public readonly uint[] Data;

    public BufferRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(PositionOps), nameof(PositionOps.Add))]
    public static BufferRef operator +(BufferRef left, BufferRef right) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(PositionOps), nameof(PositionOps.Next))]
    public static BufferRef operator ++(BufferRef operand) => throw new InvalidOperationException();

    public static implicit operator BufferRef(uint[] data) => new(data);

    public static implicit operator uint[](BufferRef value) => value.Data;
}

public static partial class OperatorMacroPositionKernels
{
    // GSH0003: a + b inside c + (...) has no storage of its own for its result
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NestedBufferKernel(uint[] left, uint[] right, uint[] output)
    {
        BufferRef a = left;
        BufferRef b = right;
        BufferRef c = output;
        c = c + (a + b);
    }

    // GSH0003: an operator macro is expanded only in an assignment statement
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void OutsideAssignmentKernel(uint[] left, uint[] right)
    {
        BufferRef a = left;
        BufferRef b = right;
        ++a;
        a++;
        BufferRef t = a + b;
    }
}

// NarrowValue converts to WideValue, but the temporary of an inner operator is passed as it is
public struct NarrowValue
{
    public int Value;

    [GpuOperatorMacro(typeof(WidthOps), nameof(WidthOps.SubNarrow))]
    public static NarrowValue operator -(NarrowValue left, NarrowValue right) => throw new InvalidOperationException();

    public static implicit operator WideValue(NarrowValue value) => new() { Value = value.Value };
}

public struct WideValue
{
    public long Value;

    [GpuOperatorMacro(typeof(WidthOps), nameof(WidthOps.SubWide))]
    public static WideValue operator -(WideValue left, WideValue right) => throw new InvalidOperationException();
}

public static partial class WidthOps
{
    [GpuDeviceFunction]
    public static void SubNarrow(ref NarrowValue result, NarrowValue left, NarrowValue right)
    {
    }

    [GpuDeviceFunction]
    public static void SubWide(ref WideValue result, WideValue left, WideValue right)
    {
    }
}

public static partial class OperatorMacroPositionKernels
{
    // GSH0003: a - b is a NarrowValue, but the operand of the outer - takes a WideValue
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConvertedNestedKernel(long[] output)
    {
        WideValue w = default;
        NarrowValue a = default;
        NarrowValue b = default;
        w = w - (a - b);
        output[0] = w.Value;
    }
}

// A cast pair with user-defined conversions around an operator macro: the expansion would drop both conversions
public struct CastValue
{
    public int Value;

    [GpuOperatorMacro(typeof(CastOps), nameof(CastOps.Sub))]
    public static CastValue operator -(CastValue left, CastValue right) => throw new InvalidOperationException();

    public static explicit operator CastProbe(CastValue value) => new() { Value = value.Value + 1000 };
}

public struct CastProbe
{
    public int Value;

    public static explicit operator CastValue(CastProbe probe) => new() { Value = probe.Value + 1 };
}

public static partial class CastOps
{
    [GpuDeviceFunction]
    public static void Sub(ref CastValue result, CastValue left, CastValue right)
    {
    }
}

public static partial class OperatorMacroPositionKernels
{
    // GSH0003: a - b is under user-defined conversions, nested and at the top level
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UserDefinedCastKernel(int[] output)
    {
        CastValue a = default;
        CastValue b = default;
        CastValue c = default;
        c = c - (CastValue)(CastProbe)(a - b);
        c = (CastValue)(CastProbe)(a - b);
        output[0] = c.Value;
    }
}
