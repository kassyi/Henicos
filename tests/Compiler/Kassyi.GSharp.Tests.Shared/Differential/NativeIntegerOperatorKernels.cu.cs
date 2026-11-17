#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels of <c>nint</c> and <c>nuint</c> operators in device code, one operator or compound assignment per kernel.
/// A native integer is a 64-bit integer there, as <c>long</c> and <c>ulong</c> are.
/// </summary>
public static partial class NativeIntegerOperatorKernels
{
    [GpuKernel]
    public static void NintAddKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v + w;
    }

    [GpuKernel]
    public static void NintSubtractKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v - w;
    }

    [GpuKernel]
    public static void NintMultiplyKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v * w;
    }

    [GpuKernel]
    public static void NintAndKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v & w;
    }

    [GpuKernel]
    public static void NintOrKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v | w;
    }

    [GpuKernel]
    public static void NintXorKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v ^ w;
    }

    [GpuKernel]
    public static void NintLessThanKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v < w ? 1 : 0;
    }

    [GpuKernel]
    public static void NintDivideKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v / w;
    }

    [GpuKernel]
    public static void NintRemainderKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        dst[t] = v % w;
    }

    [GpuKernel]
    public static void NintShiftLeftKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        int s = (int)b[t];
        dst[t] = v << s;
    }

    [GpuKernel]
    public static void NintShiftRightKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        int s = (int)b[t];
        dst[t] = v >> s;
    }

    [GpuKernel]
    public static void NintUnsignedShiftRightKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        int s = (int)b[t];
        dst[t] = v >>> s;
    }

    [GpuKernel]
    public static void NintNegateKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = -v;
    }

    [GpuKernel]
    public static void NintComplementKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        dst[t] = ~v;
    }

    [GpuKernel]
    public static void NintIncrementKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        v++;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintDecrementKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        v--;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintAddAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        v += w;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintSubtractAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        v -= w;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintMultiplyAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        v *= w;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintDivideAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        v /= w;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintRemainderAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nint w = (nint)b[t];
        v %= w;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintShiftLeftAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        int s = (int)b[t];
        v <<= s;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintShiftRightAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        int s = (int)b[t];
        v >>= s;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NintUnsignedShiftRightAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        int s = (int)b[t];
        v >>>= s;
        dst[t] = v;
    }

    [GpuKernel]
    public static void NuintAddKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        dst[t] = (long)(u + x);
    }

    [GpuKernel]
    public static void NuintSubtractKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        dst[t] = (long)(u - x);
    }

    [GpuKernel]
    public static void NuintMultiplyKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        dst[t] = (long)(u * x);
    }

    [GpuKernel]
    public static void NuintLessThanKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        dst[t] = u < x ? 1 : 0;
    }

    [GpuKernel]
    public static void NuintDivideKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        dst[t] = (long)(u / x);
    }

    [GpuKernel]
    public static void NuintRemainderKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        dst[t] = (long)(u % x);
    }

    [GpuKernel]
    public static void NuintShiftLeftKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = (int)b[t];
        dst[t] = (long)(u << s);
    }

    [GpuKernel]
    public static void NuintShiftRightKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = (int)b[t];
        dst[t] = (long)(u >> s);
    }

    [GpuKernel]
    public static void NuintUnsignedShiftRightKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = (int)b[t];
        dst[t] = (long)(u >>> s);
    }

    [GpuKernel]
    public static void NuintComplementKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        dst[t] = (long)~u;
    }

    [GpuKernel]
    public static void NuintAddAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        u += x;
        dst[t] = (long)u;
    }

    [GpuKernel]
    public static void NuintDivideAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        nuint x = (nuint)b[t];
        u /= x;
        dst[t] = (long)u;
    }

    [GpuKernel]
    public static void NuintShiftLeftAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = (int)b[t];
        u <<= s;
        dst[t] = (long)u;
    }

    [GpuKernel]
    public static void NuintShiftRightAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = (int)b[t];
        u >>= s;
        dst[t] = (long)u;
    }

    [GpuKernel]
    public static void NuintUnsignedShiftRightAssignKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = (int)b[t];
        u >>>= s;
        dst[t] = (long)u;
    }

    [GpuKernel]
    public static void NuintLeadingZeroCountKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        dst[t] = BitOperations.LeadingZeroCount(u);
    }

    [GpuKernel]
    public static void NuintTrailingZeroCountKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        dst[t] = BitOperations.TrailingZeroCount(u);
    }

    [GpuKernel]
    public static void NuintPopCountKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        dst[t] = BitOperations.PopCount(u);
    }

    [GpuKernel]
    public static void NuintLog2Kernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        dst[t] = BitOperations.Log2(u);
    }

    [GpuKernel]
    public static void NuintRotateLeftKernel(long[] a, int[] counts, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = counts[t];
        dst[t] = (long)BitOperations.RotateLeft(u, s);
    }

    [GpuKernel]
    public static void NuintRotateRightKernel(long[] a, int[] counts, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = counts[t];
        dst[t] = (long)BitOperations.RotateRight(u, s);
    }
}
