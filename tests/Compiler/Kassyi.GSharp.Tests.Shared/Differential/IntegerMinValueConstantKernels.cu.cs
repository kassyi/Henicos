#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for constant and constant-propagated <c>MinValue / -1</c> and <c>MinValue % -1</c>
/// across local variables, compound assignments, device function calls, and mixed runtime/constant operands.
/// </summary>
public static partial class IntegerMinValueConstantKernels
{
    [GpuDeviceFunction]
    private static int Quotient32(int a, int b)
    {
        return a / b;
    }

    [GpuDeviceFunction]
    private static long Quotient64(long a, long b)
    {
        return a / b;
    }

    [GpuDeviceFunction]
    private static int Remainder32(int a, int b)
    {
        return a % b;
    }

    [GpuDeviceFunction]
    private static long Remainder64(long a, long b)
    {
        return a % b;
    }

    [GpuKernel]
    public static void Int32ConstMinValueKernel(int[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        int c = a;
        int d = a;
        int e = a;
        dst[t * 8] = a / b;
        c /= b;
        dst[t * 8 + 1] = c;
        dst[t * 8 + 2] = a % b;
        d %= b;
        dst[t * 8 + 3] = d;
        dst[t * 8 + 4] = Quotient32(a, b);
        dst[t * 8 + 5] = int.MinValue / denoms[t];
        e = int.MinValue;
        dst[t * 8 + 6] = e / -1;
        dst[t * 8 + 7] = Remainder32(a, b);
    }

    [GpuKernel]
    public static void Int64ConstMinValueKernel(long[] denoms, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        long c = a;
        long d = a;
        long e = a;
        dst[t * 8] = a / b;
        c /= b;
        dst[t * 8 + 1] = c;
        dst[t * 8 + 2] = a % b;
        d %= b;
        dst[t * 8 + 3] = d;
        dst[t * 8 + 4] = Quotient64(a, b);
        dst[t * 8 + 5] = long.MinValue / denoms[t];
        e = long.MinValue;
        dst[t * 8 + 6] = e / -1L;
        dst[t * 8 + 7] = Remainder64(a, b);
    }
}
