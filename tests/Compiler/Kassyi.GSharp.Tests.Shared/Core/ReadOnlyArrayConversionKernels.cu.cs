#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct argument with <c>[GpuReadOnly]</c> array fields, which the device holds with const elements.</summary>
public struct ReadOnlyArrayHolder
{
    [GpuReadOnly] public uint[] Fixed;
    [GpuReadOnly] public uint[]? Optional;
}

/// <summary>
/// Kernels that convert a <c>[GpuReadOnly]</c> array (a field or a parameter, const on the device) to every array and
/// span type C# converts it to: a typed local, <c>Gpu.NotNull</c>, a reassigned local, a parameter of a device function,
/// <c>Span&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c> (#1709, #1071).
/// </summary>
public static partial class ReadOnlyArrayConversionKernels
{
    [GpuKernel]
    public static void TypedLocalKernel(ReadOnlyArrayHolder h, uint[] o)
    {
        uint[] x = h.Fixed;
        o[0] = x[0] + (uint)x.Length;
    }

    [GpuKernel]
    public static void NotNullLocalKernel(ReadOnlyArrayHolder h, uint[] o)
    {
        uint[] x = Gpu.NotNull(h.Optional);
        o[0] = x[1];
    }

    [GpuKernel]
    public static void ReassignedLocalKernel(ReadOnlyArrayHolder h, uint[] o)
    {
        uint[] x = o;
        x = h.Fixed;
        o[0] = x[1];
    }

    [GpuKernel]
    public static void DeviceFunctionArgumentKernel(ReadOnlyArrayHolder h, [GpuReadOnly] uint[] r, uint[] o)
    {
        o[0] = Sum(h.Fixed) + First(r) + First(h.Fixed);
    }

    [GpuKernel]
    public static void FieldToSpanKernel(ReadOnlyArrayHolder h, uint[] o)
    {
        ReadOnlySpan<uint> readOnlySpan = h.Fixed;
        Span<uint> span = h.Fixed;
        o[0] = readOnlySpan[0] + span[1];
    }

    [GpuKernel]
    public static void ParameterToReadOnlySpanKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        ReadOnlySpan<uint> implicitSpan = r;
        ReadOnlySpan<uint> castSpan = (ReadOnlySpan<uint>)r;
        o[0] = implicitSpan[0] + castSpan[1] + (uint)castSpan.Length;
    }

    [GpuDeviceFunction]
    private static uint Sum(uint[] a)
    {
        uint sum = 0u;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i];
        }

        return sum;
    }

    [GpuDeviceFunction]
    private static uint First(ReadOnlySpan<uint> s) => s[0];
}
