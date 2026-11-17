#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels of the conversions between <c>nint</c> or <c>nuint</c> and the other numeric types, one per kernel.
/// </summary>
public static partial class NativeIntegerConversionKernels
{
    [GpuKernel]
    public static void NintToFloatKernel(long[] a, float[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = (float)v;
    }

    [GpuKernel]
    public static void NintToDoubleKernel(long[] a, double[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = (double)v;
    }

    [GpuKernel]
    public static void NintToIntKernel(long[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = (int)v;
    }

    [GpuKernel]
    public static void NintToUIntKernel(long[] a, uint[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = (uint)v;
    }

    [GpuKernel]
    public static void NintToULongKernel(long[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = (ulong)v;
    }

    [GpuKernel]
    public static void NuintToFloatKernel(long[] a, float[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint v = (nuint)a[t];
        dst[t] = (float)v;
    }

    [GpuKernel]
    public static void NuintToDoubleKernel(long[] a, double[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint v = (nuint)a[t];
        dst[t] = (double)v;
    }

    [GpuKernel]
    public static void NuintToIntKernel(long[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint v = (nuint)a[t];
        dst[t] = (int)v;
    }

    [GpuKernel]
    public static void NuintToUIntKernel(long[] a, uint[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint v = (nuint)a[t];
        dst[t] = (uint)v;
    }

    [GpuKernel]
    public static void NuintToULongKernel(long[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint v = (nuint)a[t];
        dst[t] = (ulong)v;
    }

    [GpuKernel]
    public static void NintToLongKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = (long)v;
    }

    [GpuKernel]
    public static void NintFromIntKernel(int[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nint)a[t];
    }

    [GpuKernel]
    public static void NintFromUIntKernel(uint[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nint)a[t];
    }

    [GpuKernel]
    public static void NintFromULongKernel(ulong[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nint)a[t];
    }

    [GpuKernel]
    public static void NintFromFloatKernel(float[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nint)a[t];
    }

    [GpuKernel]
    public static void NintFromDoubleKernel(double[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nint)a[t];
    }

    [GpuKernel]
    public static void NuintFromIntKernel(int[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nuint)a[t];
    }

    [GpuKernel]
    public static void NuintFromUIntKernel(uint[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nuint)a[t];
    }

    [GpuKernel]
    public static void NuintFromULongKernel(ulong[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nuint)a[t];
    }

    [GpuKernel]
    public static void NuintFromFloatKernel(float[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nuint)a[t];
    }

    [GpuKernel]
    public static void NuintFromDoubleKernel(double[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nuint)a[t];
    }

    [GpuKernel]
    public static void NuintFromLongKernel(long[] a, ulong[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (nuint)a[t];
    }
}
