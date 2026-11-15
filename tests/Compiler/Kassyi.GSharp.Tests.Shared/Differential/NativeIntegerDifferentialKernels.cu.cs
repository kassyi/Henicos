#nullable enable
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
}
