#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>Nested struct for testing early null check on struct field path.</summary>
public struct DeviceFunctionInnerBag
{
    public int[] Data;
}

/// <summary>Outer struct holding a nested struct and another array field.</summary>
public struct DeviceFunctionOuterBag
{
    public DeviceFunctionInnerBag Inner;
    public int[] Other;
}

public static partial class DeviceFunctionEarlyNullCheckKernels
{
    [GpuDeviceFunction]
    public static int ProbeDirect(int[] arr) => arr.Length;

    [GpuKernel]
    public static void DirectKernel(int[]? input, int[] output)
    {
        output[0] = ProbeDirect(input!);
    }

    [GpuDeviceFunction]
    public static int LeafCallee(int[] x) => x.Length;

    [GpuDeviceFunction]
    public static int OuterCaller(int[]? x) => LeafCallee(x!);

    [GpuKernel]
    public static void NestedCallerKernel(int[]? input, int[] output)
    {
        output[0] = OuterCaller(input);
    }

    [GpuDeviceFunction]
    public static int ProbeStruct(DeviceFunctionOuterBag s) => s.Inner.Data.Length + s.Other.Length;

    [GpuKernel]
    public static void StructFieldKernel(DeviceFunctionOuterBag bag, int[] output)
    {
        output[0] = ProbeStruct(bag);
    }
}
