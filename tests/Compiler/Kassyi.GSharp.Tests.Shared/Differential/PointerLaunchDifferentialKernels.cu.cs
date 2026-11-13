#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// The array lengths a kernel sees when it is launched through Launch{Kernel}Ptr (#720)
public static partial class PointerLaunchDifferentialKernels
{
    // Each array sees its own length: a length taken from the wrong argument changes the lengths or the sums
    [GpuKernel]
    public static void TwoLengthsKernel(int[] a, int[] b, [Out] int[] lengths)
    {
        int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        if (tid == 0)
        {
            lengths[0] = a.Length;
            lengths[1] = b.Length;
        }

        if (tid < a.Length && tid < b.Length)
        {
            a[tid] += b[tid];
        }
    }

    // A null optional array, an empty one and a filled one, each passed as C# passes it
    [GpuKernel]
    public static void OptionalLengthKernel(int[]? optional, [Out] int[] result)
    {
        if ((BlockIndex.X * BlockDimension.X) + ThreadIndex.X == 0)
        {
            result[0] = optional is null ? -1 : optional.Length;
        }
    }

    // An overloaded kernel gets a typed pointer launcher, whose buffer of a nullable array may be null
    [GpuKernel]
    public static void OverloadedOptionalKernel(int[]? optional, [Out] int[] result)
    {
        if ((BlockIndex.X * BlockDimension.X) + ThreadIndex.X == 0)
        {
            result[0] = optional is null ? -1 : optional.Length;
        }
    }

    [GpuKernel]
    public static void OverloadedOptionalKernel(float[]? optional, [Out] int[] result)
    {
        if ((BlockIndex.X * BlockDimension.X) + ThreadIndex.X == 0)
        {
            result[0] = optional is null ? -2 : optional.Length;
        }
    }
}
