using Kassyi.GSharp.Core;
using System;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Generator;

public class TestKernelReturnTypesSadPath
{
    // Managed array
    [GpuKernel]
    public static float[] ArrayKernel() => new float[0];

    // Reference type
    [GpuKernel]
    public static string StringKernel() => "";

    // Pointer type
    public unsafe static float* PointerKernel() => null;

    // GpuKernel on pointers
    [GpuKernel]
    public unsafe static float* PointerKernel2() => null;

    // Struct containing managed references
    public struct ManagedStruct
    {
        public string S;
    }

    [GpuKernel]
    public static ManagedStruct ManagedStructKernel() => default;

    // Ref-like type
    [GpuKernel]
    public static ReadOnlySpan<float> SpanKernel() => default;
}
