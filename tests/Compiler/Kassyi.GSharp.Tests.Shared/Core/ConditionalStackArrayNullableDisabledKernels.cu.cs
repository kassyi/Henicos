#nullable disable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The same conditional as <see cref="ConditionalStackArrayKernels.ConditionalKernel"/> in a file without nullable
/// annotations.
/// </summary>
public static partial class ConditionalStackArrayNullableDisabledKernels
{
    [GpuKernel]
    public static void ConditionalKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? a : input);
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
