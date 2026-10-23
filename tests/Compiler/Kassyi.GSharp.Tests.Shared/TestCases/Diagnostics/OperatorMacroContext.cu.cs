using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct OperatorMacroState
{
    public int State;
}

public static partial class OperatorMacroContextTargets
{
    [GpuDeviceFunction]
    public static void Add(ref OperatorMacroState ctx, uint[] result, uint[] left, uint[] right)
    {
    }
}

public readonly ref struct ContextValue
{
    public readonly uint[] Data;

    public ContextValue(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(OperatorMacroContextTargets), nameof(OperatorMacroContextTargets.Add))]
    public static ContextValue operator +(ContextValue a, ContextValue b) => throw new InvalidOperationException();

    public static implicit operator ContextValue(uint[] data) => new(data);
}

public static partial class OperatorMacroContextKernels
{
    // GSH0003: no parameter or local of type OperatorMacroState is in scope
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NoContextKernel(uint[] left, uint[] right, uint[] result)
    {
        ContextValue a = left;
        ContextValue b = right;
        ContextValue c = result;
        c = a + b;
    }

    // GSH0003: two variables of type OperatorMacroState are in scope; the one declared after the call is not counted
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TwoContextsKernel(OperatorMacroState first, uint[] left, uint[] right, uint[] result)
    {
        OperatorMacroState second = first;
        ContextValue a = left;
        ContextValue b = right;
        ContextValue c = result;
        c = a + b;
        OperatorMacroState third = second;
        result[0] = (uint)third.State;
    }
}
