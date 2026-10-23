using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class OperatorMacroTargets
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
    }
}

// GSH0003: the operator macro names a target method that does not exist
public readonly ref struct MissingTargetValue
{
    public readonly uint[] Data;

    public MissingTargetValue(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(OperatorMacroTargets), "Missing")]
    public static MissingTargetValue operator +(MissingTargetValue a, MissingTargetValue b) => throw new InvalidOperationException();

    public static implicit operator MissingTargetValue(uint[] data) => new(data);
}

public static partial class OperatorMacroMissingTargetKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MissingTargetKernel(uint[] left, uint[] right, uint[] result)
    {
        MissingTargetValue a = left;
        MissingTargetValue b = right;
        MissingTargetValue c = result;
        c = a + b;
    }
}
