using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct OverloadCtx
{
    public int State;
}

// No overload applies: OverloadValue converts to neither OverloadCtx nor int, and not to uint[] either
public static partial class NoOverloadOps
{
    [GpuDeviceFunction]
    public static void Shift(OverloadCtx ctx, int wordShift, int bitShift)
    {
    }

    [GpuDeviceFunction]
    public static void Shift(ref OverloadCtx ctx, uint[] result, uint[] value, int shift)
    {
    }
}

// Two overloads apply equally well, one with a context and one without
public static partial class AmbiguousOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
    }

    [GpuDeviceFunction]
    public static void Add(ref OverloadCtx ctx, uint[] result, uint[] left, uint[] right)
    {
    }
}

// A ref parameter takes only a variable of its own type: OverloadRef is not uint[]
public static partial class RefResultOps
{
    [GpuDeviceFunction]
    public static void Add(ref uint[] result, uint[] left, uint[] right)
    {
    }
}

// The result parameter takes OneWayRef, but the assignment target is a uint[], which does not convert to it
public static partial class TargetOps
{
    [GpuDeviceFunction]
    public static void Add(OneWayRef result, OneWayRef left, OneWayRef right)
    {
    }
}

public readonly ref struct OverloadValue
{
    public readonly uint[] Data;

    public OverloadValue(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(NoOverloadOps), nameof(NoOverloadOps.Shift))]
    public static OverloadValue operator <<(OverloadValue value, int shift) => throw new InvalidOperationException();

    public static implicit operator OverloadValue(uint[] data) => new(data);
}

public readonly ref struct OverloadRef
{
    public readonly uint[] Data;

    public OverloadRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(AmbiguousOps), nameof(AmbiguousOps.Add))]
    public static OverloadRef operator +(OverloadRef left, OverloadRef right) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(RefResultOps), nameof(RefResultOps.Add))]
    public static OverloadRef operator -(OverloadRef left, OverloadRef right) => throw new InvalidOperationException();

    public static implicit operator OverloadRef(uint[] data) => new(data);

    public static implicit operator uint[](OverloadRef value) => value.Data;
}

// Converts to uint[], but not back
public readonly ref struct OneWayRef
{
    public readonly uint[] Data;

    public OneWayRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(TargetOps), nameof(TargetOps.Add))]
    public static OneWayRef operator *(OneWayRef left, OneWayRef right) => throw new InvalidOperationException();

    public static implicit operator uint[](OneWayRef value) => value.Data;
}

public static partial class OperatorMacroOverloadKernels
{
    // GSH0003: no overload of NoOverloadOps.Shift takes (OverloadValue, OverloadValue, int)
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NoOverloadKernel(uint[] input, uint[] output)
    {
        OverloadCtx ctx = default;
        OverloadValue x = input;
        OverloadValue r = output;
        r = x << 3;
    }

    // GSH0003: AmbiguousOps.Add is ambiguous; a context in scope does not decide it
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AmbiguousKernel(uint[] left, uint[] right, uint[] output)
    {
        OverloadCtx ctx = default;
        OverloadRef a = left;
        OverloadRef b = right;
        OverloadRef c = output;
        c = a + b;
    }

    // GSH0003: the ref result parameter of RefResultOps.Add needs a uint[], not an OverloadRef
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void RefResultKernel(uint[] left, uint[] right, uint[] output)
    {
        OverloadRef a = left;
        OverloadRef b = right;
        OverloadRef c = output;
        c = a - b;
    }

    // GSH0003: TargetOps.Add takes the operator's own types, but raw is a uint[], which does not convert to OneWayRef
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TargetKernel(uint[] left, uint[] right, uint[] output)
    {
        OneWayRef a = new OneWayRef(left);
        OneWayRef b = new OneWayRef(right);
        uint[] raw = output;
        raw = a * b;
    }
}

// int converts to long (built-in) and to ShiftCount (user-defined), and neither of long and ShiftCount converts to the
// other: C# reports the call as ambiguous (CS0121); a built-in conversion is not better than a user-defined one
public struct ShiftCount
{
    public int Value;

    public static implicit operator ShiftCount(int value) => new() { Value = value };
}

public static partial class ShlOps
{
    [GpuDeviceFunction]
    public static void Shl(uint[] result, uint[] value, long shift)
    {
    }

    [GpuDeviceFunction]
    public static void Shl(uint[] result, uint[] value, ShiftCount shift)
    {
    }
}

// uint[] and MutualRef convert to each other, so neither is the better conversion target
public static partial class MutualOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
    }

    [GpuDeviceFunction]
    public static void Add(MutualRef result, MutualRef left, MutualRef right)
    {
    }
}

// An 'in' or 'ref readonly' parameter takes only its own type: OverloadRef is not uint[]
public static partial class InOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, in uint[] left, uint[] right)
    {
    }
}

public static partial class RefReadOnlyOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, ref readonly uint[] left, uint[] right)
    {
    }
}

public readonly ref struct MutualRef
{
    public readonly uint[] Data;

    public MutualRef(uint[] data) => Data = data;

    public static implicit operator MutualRef(uint[] data) => new(data);

    public static implicit operator uint[](MutualRef value) => value.Data;
}

public readonly ref struct ConversionValue
{
    public readonly uint[] Data;

    public ConversionValue(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(ShlOps), nameof(ShlOps.Shl))]
    public static ConversionValue operator <<(ConversionValue value, int shift) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(MutualOps), nameof(MutualOps.Add))]
    public static ConversionValue operator +(ConversionValue left, ConversionValue right) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(InOps), nameof(InOps.Add))]
    public static ConversionValue operator -(ConversionValue left, ConversionValue right) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(RefReadOnlyOps), nameof(RefReadOnlyOps.Add))]
    public static ConversionValue operator *(ConversionValue left, ConversionValue right) => throw new InvalidOperationException();

    public static implicit operator ConversionValue(uint[] data) => new(data);

    public static implicit operator uint[](ConversionValue value) => value.Data;

    public static implicit operator MutualRef(ConversionValue value) => new(value.Data);
}

public static partial class OperatorMacroOverloadKernels
{
    // GSH0003: ShlOps.Shl is ambiguous between the long and the ShiftCount overloads
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BuiltInAgainstUserDefinedKernel(uint[] input, uint[] output)
    {
        ConversionValue x = input;
        ConversionValue r = output;
        r = x << 3;
    }

    // GSH0003: MutualOps.Add is ambiguous; uint[] and MutualRef convert to each other
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MutualConversionKernel(uint[] left, uint[] right, uint[] output)
    {
        ConversionValue a = left;
        ConversionValue b = right;
        ConversionValue c = output;
        c = a + b;
    }

    // GSH0003: the 'in' and the 'ref readonly' parameter need a uint[], not a ConversionValue
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ReadOnlyReferenceKernel(uint[] left, uint[] right, uint[] output)
    {
        ConversionValue a = left;
        ConversionValue b = right;
        ConversionValue c = output;
        c = a - b;
        c = a * b;
    }
}
