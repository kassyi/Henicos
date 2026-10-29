#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels whose array arguments a caller may set to the same array, also together with a struct field (#721). Each
/// kernel writes an element through one holder and then reads it through another, so a launcher that gives each holder
/// its own copy reads the old value, and its copy back of the second copy loses the write through the first.
/// </summary>
/// <summary>An enum over int, whose array the runtime also lets a caller pass as an int[].</summary>
public enum SharedArrayTag
{
    None,
    First
}

public static partial class SharedArrayArgumentKernels
{
    // C#: when a and b are one array, the write through a is seen through b
    [GpuKernel]
    public static void TwoArgumentsKernel(int[] a, int[] b)
    {
        if (ThreadIndex.X == 0)
        {
            a[0] = 5;
            b[1] = b[0] + a[0];
        }
    }

    // Optional arguments: a null or empty array has no device region, so there is nothing to share
    [GpuKernel]
    public static void OptionalArgumentsKernel(int[]? a, int[]? b, int[] lengths)
    {
        if (ThreadIndex.X == 0)
        {
#pragma warning disable CS8602 // The device reads a null optional array argument as one of length 0
            lengths[0] = a.Length;
            lengths[1] = b.Length;
#pragma warning restore CS8602
        }
    }

    // The same through a struct field and an array argument
    [GpuKernel]
    public static void FieldAndArgumentKernel(SharedArrayHolder h, int[] a)
    {
        if (ThreadIndex.X == 0)
        {
            h.Data[0] = 4;
            a[1] = a[0] + h.Data[0];
        }
    }

    // A [GpuReadOnly] argument sees the write through a writable argument that holds the same array
    [GpuKernel]
    public static void ReadOnlyAndWritableKernel([GpuReadOnly] int[] r, int[] w)
    {
        if (ThreadIndex.X == 0)
        {
            w[0] = 6;
            w[1] = r[0] + 1;
        }
    }

    // An [In] argument sees the write through a writable argument, which copies the array back
    [GpuKernel]
    public static void InAndWritableKernel([In] int[] input, int[] output)
    {
        if (ThreadIndex.X == 0)
        {
            output[0] = 2;
            output[1] = input[0] + 1;
        }
    }

    // An [Out] argument before an [In] argument: the array is still copied in for the [In] one
    [GpuKernel]
    public static void OutBeforeInKernel([Out] int[] output, [In] int[] input)
    {
        if (ThreadIndex.X == 0)
        {
            output[1] = input[0] + 1;
        }
    }

    // Two [In] arguments: the write through one is seen through the other, and neither copies the array back
    [GpuKernel]
    public static void TwoInKernel([In] int[] first, [In] int[] second, int[] result)
    {
        if (ThreadIndex.X == 0)
        {
            first[0] = 9;
            result[0] = second[0];
        }
    }

    // Three holders: a struct field, a writable argument and an [In] argument
    [GpuKernel]
    public static void ThreeHoldersKernel(SharedArrayHolder h, int[] a, [In] int[] b)
    {
        if (ThreadIndex.X == 0)
        {
            h.Data[0] = 1;
            a[1] = h.Data[0] + 1;
            a[2] = b[0] + b[1] + 1;
        }
    }

    // Two arguments of elements whose layout the launcher converts
    [GpuKernel]
    public static void ConvertedArgumentsKernel(ConvertBag[] first, ConvertBag[] second)
    {
        if (ThreadIndex.X == 0)
        {
            first[0].N = 7;
            second[1].N = second[0].N + first[0].N;
        }
    }

    // An [In] int[] and a writable uint[] that are one array ((uint[])(object)x): reference identity ignores the
    // element type, so the [In] argument still sees the write
    [GpuKernel]
    public static void InIntAndWritableUIntKernel([In] int[] input, uint[] output)
    {
        if (ThreadIndex.X == 0)
        {
            output[0] = 5;
            output[1] = (uint)input[0] / 5u;
        }
    }

    // The same with an enum array and an array of its underlying type
    [GpuKernel]
    public static void InEnumAndWritableIntKernel([In] SharedArrayTag[] tags, int[] values)
    {
        if (ThreadIndex.X == 0)
        {
            values[0] = 3;
            values[1] = (int)tags[0] + 1;
        }
    }

    // Two [In] arguments of different element types that are one array share one copy on the CPU
    [GpuKernel]
    public static void TwoInDifferentTypesKernel([In] int[] first, [In] uint[] second, uint[] result)
    {
        if (ThreadIndex.X == 0)
        {
            first[0] = 9;
            result[0] = second[0];
        }
    }
}
