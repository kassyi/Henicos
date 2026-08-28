using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN28_MethodAttributesTestCases
{
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AggressiveInlinedFunc(int x)
    {
        return x + 1;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NoInlinedFunc(int x)
    {
        return x * 2;
    }

    [GpuDeviceFunction]
    [GpuInline]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707")]
    public static int MultipleAttributesFunc(int x)
    {
        return x + 10;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.NoOptimization)]
    public static int NoOptimizationFunc(int x)
    {
        return x - 1;
    }
}
