// ReSharper disable All
#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Stack arrays passed to non-nullable array parameters with the nullable context on, as in a user project (#1543):
/// a stack array is never null, so the call needs no argument check (which a C array could not take).
/// </summary>
public static partial class NullableStackArrayKernels
{
    [GpuDeviceFunction]
    public static int SumArray(int[] values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    [GpuDeviceFunction]
    public static int First(int[] values, int[] fallback)
    {
        return values.Length > 0 ? values[0] : fallback[0];
    }

    [GpuKernel]
    public static void StackArrayArgumentKernel(int[] input, int[] output)
    {
        int[] a = new int[10];
        a[3] = 4;
        int[] b = new int[2];
        b[0] = 9;
        output[0] = SumArray(a);
        // A stack array and a checked kernel parameter in one call
        output[1] = First(b, input);
        // A kernel parameter alone keeps working as before
        output[2] = SumArray(input);
    }
}
