// ReSharper disable All
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class MethodAttributesKernels
{
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InlinedAdd(int a, int b)
    {
        return a + b;
    }

    [GpuDeviceFunction]
    [GpuInline]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707")]
    public static int MultiAttributeMultiply(int a, int b)
    {
        return a * b;
    }

    [GpuKernel]
    public static void MethodAttributesKernel(int[] input, int[] result)
    {
        int tid = ThreadIndex.X;
        int sum = InlinedAdd(input[tid], 100);
        result[tid] = MultiAttributeMultiply(sum, 2);
    }
}
