#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for <c>nint</c> and <c>nuint</c> in device code: a constant, a local read through <c>var</c>,
/// casts, sorts and parameters (the operators, conversions and storage forms have kernel classes of their own). They
/// are 64-bit integers there, as <c>long</c> and <c>ulong</c> are.
/// </summary>
public static partial class NativeIntegerDifferentialKernels
{
    private const nint Five = 5;

    private const nuint Big = 0xFFFF_FFF0;

    [GpuKernel]
    public static void NativeIntegerConstantKernel(long[] dst, int x)
    {
        var n = Five;
        var u = Big;
        dst[0] = n << 40;
        dst[1] = (nint)x;
        dst[2] = (long)(u >> 4);
        dst[3] = (long)(u + 16);
        dst[4] = (long)((nuint)x * 3);
        dst[5] = (long)(~(nuint)0 >> 60);
    }

    [GpuKernel]
    public static void NativeIntegerSortKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int col = t * 4;
        Span<nint> s = [(nint)a[col], (nint)a[col + 1], (nint)a[col + 2], (nint)a[col + 3]];
        s.Sort();
        dst[col] = s[0];
        dst[col + 1] = s[1];
        dst[col + 2] = s[2];
        dst[col + 3] = s[3];
    }

    [GpuKernel]
    public static void NativeUnsignedIntegerSortKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int col = t * 4;
        Span<nuint> s = [(nuint)a[col], (nuint)a[col + 1], (nuint)a[col + 2], (nuint)a[col + 3]];
        s.Sort();
        dst[col] = (long)s[0];
        dst[col + 1] = (long)s[1];
        dst[col + 2] = (long)s[2];
        dst[col + 3] = (long)s[3];
    }

    [GpuKernel]
    public static void NativeIntegerParameterKernel(long[] dst, nint p, nuint q)
    {
        dst[0] = p;
        dst[1] = (long)q;
        dst[2] = p + 1;
        dst[3] = (long)(q >> 1);
        dst[4] = (long)(q * 2);
        dst[5] = p < 0 ? 1 : 0;
    }
}
