#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for <c>nint</c> and <c>nuint</c> in device code: a constant, a local read through <c>var</c>,
/// casts, arithmetic and shifts. They are 64-bit integers there, as <c>long</c> and <c>ulong</c> are.
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
    public static void NativeIntegerArithmeticKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint v = (nint)a[t];
        nuint w = (nuint)a[t];
        nint divisor = (nint)((a[t] & 7) + 1);

        int col = t * 6;
        dst[col + 0] = v + 3;
        dst[col + 1] = v / divisor;
        dst[col + 2] = v % divisor;
        dst[col + 3] = (long)(w * 5);
        dst[col + 4] = (long)(w / (nuint)divisor);
        dst[col + 5] = v < 0 ? 1 : (long)(w >> 3);
    }

    /// <summary>Columns of <see cref="NativeIntegerBitOperationsKernel"/>.</summary>
    public static readonly string[] BitOperationsColumns =
    [
        "lzcnt_nuint", "tzcnt_nuint", "popcnt_nuint", "log2_nuint", "rotl_nuint", "rotr_nuint"
    ];

    [GpuKernel]
    public static void NativeIntegerBitOperationsKernel(long[] a, int[] counts, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nuint u = (nuint)a[t];
        int s = counts[t];

        int col = t * 6;
        dst[col + 0] = BitOperations.LeadingZeroCount(u);
        dst[col + 1] = BitOperations.TrailingZeroCount(u);
        dst[col + 2] = BitOperations.PopCount(u);
        dst[col + 3] = BitOperations.Log2(u);
        dst[col + 4] = (long)BitOperations.RotateLeft(u, s);
        dst[col + 5] = (long)BitOperations.RotateRight(u, s);
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
