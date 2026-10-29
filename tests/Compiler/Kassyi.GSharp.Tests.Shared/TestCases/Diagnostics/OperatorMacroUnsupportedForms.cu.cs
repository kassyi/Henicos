using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class FormOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
    }

    [GpuDeviceFunction]
    public static void Next(uint[] result, uint[] operand)
    {
    }

    [GpuDeviceFunction]
    public static void Mul<T>(T result, T left, T right)
    {
    }

    [GpuDeviceFunction]
    public static void AddValues(ref FormValue result, FormValue left, FormValue right)
    {
    }
}

public readonly ref struct FormRef
{
    public readonly uint[] Data;

    public FormRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(FormOps), nameof(FormOps.Add))]
    public static FormRef operator +(FormRef left, FormRef right) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(FormOps), nameof(FormOps.Next))]
    public static FormRef operator ++(FormRef operand) => throw new InvalidOperationException();

    [GpuOperatorMacro(typeof(FormOps), nameof(FormOps.Mul))]
    public static FormRef operator *(FormRef left, FormRef right) => throw new InvalidOperationException();

    public static implicit operator FormRef(uint[] data) => new(data);

    public static implicit operator uint[](FormRef value) => value.Data;
}

public struct FormValue
{
    public uint Value;

    [GpuOperatorMacro(typeof(FormOps), nameof(FormOps.AddValues))]
    public static FormValue operator +(FormValue left, FormValue right) => throw new InvalidOperationException();
}

public static partial class OperatorMacroUnsupportedFormKernels
{
    // GSH0003: c += a + b would call Add(c, a, b), which computes c = a + b; c += a has no call to expand into
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CompoundAssignmentKernel(uint[] left, uint[] right, uint[] output)
    {
        FormRef a = left;
        FormRef b = right;
        FormRef c = output;
        c += a + b;
        c += a;
    }

    // GSH0003: ++ assigns its operand
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void IncrementKernel(uint[] input, uint[] output)
    {
        FormRef x = input;
        FormRef r = output;
        r = ++x;
    }

    // GSH0003: + on FormValue? is the lifted form of the operator macro
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LiftedOperatorKernel(uint[] output)
    {
        FormValue? x = new FormValue { Value = 1u };
        FormValue? y = new FormValue { Value = 2u };
        FormValue z = default;
        z = (FormValue)(x + y);
        output[0] = z.Value;
    }

    // GSH0003: FormOps.Mul is generic, so it is not a candidate
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GenericTargetKernel(uint[] left, uint[] right, uint[] output)
    {
        FormRef a = left;
        FormRef b = right;
        FormRef c = output;
        c = a * b;
    }

    // GSH0003: c has no storage for the target to write into (#711: the managed expansion would not compile)
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnassignedTargetKernel(uint[] left, uint[] right)
    {
        FormRef a = left;
        FormRef b = right;
        FormRef c;
        c = a + b;
    }

    // GSH0003: an out parameter is not assigned before its first assignment either
    [GpuDeviceFunction]
    public static void AddInto(out FormValue result, FormValue left, FormValue right)
    {
        result = left + right;
    }
}

public struct FormOutValue
{
    public uint Value;

    [GpuOperatorMacro(typeof(FormOutOps), nameof(FormOutOps.Add))]
    public static FormOutValue operator +(FormOutValue left, FormOutValue right) => throw new InvalidOperationException();
}

public static partial class FormOutOps
{
    [GpuDeviceFunction]
    public static void Add(out FormOutValue result, FormOutValue left, FormOutValue right)
    {
        result.Value = left.Value + right.Value;
    }
}

public struct FormHolder
{
    public FormValue Inner;
    public uint Other;
}

public static partial class OperatorMacroTargetAssignmentKernels
{
    // No GSH0003: the target method assigns its out result
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void OutResultKernel(uint[] output)
    {
        FormOutValue a = default;
        FormOutValue c;
        c = a + a;
        output[0] = c.Value;
    }

    // No GSH0003: an out parameter that the target method assigns
    [GpuDeviceFunction]
    public static void SumInto(out FormOutValue result, FormOutValue left)
    {
        result = left + left;
    }

    // No GSH0003: the local function assigns a local that the method assigned (not where declared) before calling it
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LocalFunctionKernel(uint[] output)
    {
        FormValue a = default, c;
        c = default;
        void Add()
        {
            c = a + a;
        }

        Add();
        output[0] = c.Value;
    }

    // No GSH0003: both branches assign the target
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BranchesKernel(uint[] input, uint[] output)
    {
        FormValue a = default;
        FormValue c;
        if (input[0] > 0u)
        {
            c = a;
        }
        else
        {
            c = default;
        }

        c = a + a;
        output[0] = c.Value;
    }

    // GSH0003: a field of a struct local that is not fully assigned has no storage either
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void PartlyAssignedStructKernel(uint[] output)
    {
        FormValue a = default;
        FormHolder h;
        h.Other = 0u;
        h.Inner = a + a;
        output[0] = h.Other;
    }
}
