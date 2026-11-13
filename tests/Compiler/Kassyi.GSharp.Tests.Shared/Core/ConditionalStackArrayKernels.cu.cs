#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that pass a conditional of a stack array local and a kernel array to an array parameter: the stack array is a
/// C++ array, which the C++ conditional would decay to a pointer, so each branch is written as the span it converts to.
/// </summary>
public static partial class ConditionalStackArrayKernels
{
    [GpuKernel]
    public static void ConditionalKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? a : input);
    }

    // The branches the other way round, and a stack array on both branches
    [GpuKernel]
    public static void ReversedAndBothStackKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        int[] b = [7, 8];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? input : a);
        output[1] = SumArray(flag[0] > 1 ? a : b);
    }

    [GpuDeviceFunction]
    private static int SumArray(int[] values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }
}
