#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct with two array fields, which a caller may set to the same array.</summary>
public struct SharedArrayPair
{
    public int[] A;
    public int[] B;
}

/// <summary>A struct with one array field.</summary>
public struct SharedArrayHolder
{
    public int[] Data;
}

public static partial class SharedArrayFieldKernels
{
    // C#: when A and B are one array, the write through A is seen through B
    [GpuKernel]
    public static void TwoFieldsKernel(SharedArrayPair p)
    {
        if (ThreadIndex.X == 0)
        {
            p.A[0] = 1;
            p.B[1] = p.A[0] + p.B[0];
        }
    }

    // The same through the fields of two struct arguments
    [GpuKernel]
    public static void TwoStructsKernel(SharedArrayHolder first, SharedArrayHolder second)
    {
        if (ThreadIndex.X == 0)
        {
            first.Data[0] = 3;
            second.Data[1] = first.Data[0] + second.Data[0];
        }
    }

    // The same through the array fields of two elements of a converted array
    [GpuKernel]
    public static void ElementFieldsKernel(ConvertBag[] bags)
    {
        if (ThreadIndex.X == 0)
        {
            bags[0].Data[0] = 5;
            bags[1].Data[1] = bags[0].Data[0] + bags[1].Data[0];
        }
    }
}
