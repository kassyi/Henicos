#nullable enable
#pragma warning disable CS8602 // The device reads a null optional array as one of length 0
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

public struct NullCheckHolder
{
    public int[] Data;
}

// Which array reads get the null check (__gsharp_array_ref, #706): a receiver that may be null does; a kernel array
// parameter that the kernel never writes, a stack array local and a span do not
[SkipSnapshotConsistency("Array null checks checked by EmissionShapeTests")]
public static partial class ArrayNullCheckShapeKernels
{
    [GpuKernel]
    public static void ElidedKernel(int[] values, int[]? bias, int[] o)
    {
        int[] local = new int[4];
        local[0] = values.Length;
        o[0] = values[0] + local.Length + local[0];
        o[1] = bias.Length;
        foreach (var v in values)
        {
            o[2] += v;
        }

        o[3] = (o[0] > 0 ? values : o).Length;
    }

    [GpuKernel]
    public static void CheckedKernel(NullCheckHolder h, int[] written, int[] o)
    {
        written = h.Data;
        int[] copy = h.Data;
        o[0] = h.Data.Length;
        o[1] = written[0];
        o[2] = copy.Length;
        o[3] = Sum(copy);
        System.ReadOnlySpan<int> span = h.Data;
        o[4] = span.Length + (h.Data is null ? 1 : 0);
        o[5] = (o[0] > 0 ? o : null).Length;
    }

    [GpuDeviceFunction]
    private static int Sum(int[] a)
    {
        int s = 0;
        foreach (var v in a)
        {
            s += v;
        }

        return s;
    }
}
