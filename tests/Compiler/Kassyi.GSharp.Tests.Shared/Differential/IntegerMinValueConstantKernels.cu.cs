#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for constant and constant-propagated <c>MinValue / -1</c> and <c>MinValue % -1</c>. Each kernel
/// is one shape (<c>a = MinValue</c>, <c>b = -1</c> from locals, arguments or a runtime divisor) and has an
/// <c>int</c> and a <c>long</c> version. The remainder shapes use the result in an expression before it is stored:
/// nvcc folds a stored <c>MinValue % -1</c> to 0 but uses the undefined value in the surrounding expression.
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
    public static void Int32DivKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = a / b;
    }

    [GpuKernel]
    public static void Int32DivAssignKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        int c = a;
        c /= b;
        dst[t] = c;
    }

    [GpuKernel]
    public static void Int32RemKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = a % b;
    }

    [GpuKernel]
    public static void Int32RemAssignKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        int d = a;
        d %= b;
        dst[t] = d;
    }

    [GpuKernel]
    public static void Int32DivCallKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = Quotient32(a, b);
    }

    [GpuKernel]
    public static void Int32RemCallKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = Remainder32(a, b);
    }

    [GpuKernel]
    public static void Int32DivRuntimeDivisorKernel(int[] denoms, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = int.MinValue / denoms[t];
    }

    [GpuKernel]
    public static void Int32DivConstDivisorKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int e = int.MinValue;
        dst[t] = e / -1;
    }

    [GpuKernel]
    public static void Int32RemPlusFiveKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = (a % b) + 5;
    }

    [GpuKernel]
    public static void Int32RemCompareKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = (a % b) == 0 ? 1 : 2;
    }

    [GpuKernel]
    public static void Int32RemCallCompareKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = Remainder32(a, b) == 0 ? 1 : 2;
    }

    [GpuKernel]
    public static void Int32DivShiftKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int a = int.MinValue;
        int b = -1;
        dst[t] = (a / b) >> 31;
    }

    [GpuKernel]
    public static void Int64DivKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = a / b;
    }

    [GpuKernel]
    public static void Int64DivAssignKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        long c = a;
        c /= b;
        dst[t] = c;
    }

    [GpuKernel]
    public static void Int64RemKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = a % b;
    }

    [GpuKernel]
    public static void Int64RemAssignKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        long d = a;
        d %= b;
        dst[t] = d;
    }

    [GpuKernel]
    public static void Int64DivCallKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = Quotient64(a, b);
    }

    [GpuKernel]
    public static void Int64RemCallKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = Remainder64(a, b);
    }

    [GpuKernel]
    public static void Int64DivRuntimeDivisorKernel(long[] denoms, long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        dst[t] = long.MinValue / denoms[t];
    }

    [GpuKernel]
    public static void Int64DivConstDivisorKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long e = long.MinValue;
        dst[t] = e / -1L;
    }

    [GpuKernel]
    public static void Int64RemPlusFiveKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = (a % b) + 5L;
    }

    [GpuKernel]
    public static void Int64RemCompareKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = (a % b) == 0 ? 1L : 2L;
    }

    [GpuKernel]
    public static void Int64RemCallCompareKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = Remainder64(a, b) == 0 ? 1L : 2L;
    }

    [GpuKernel]
    public static void Int64DivShiftKernel(long[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        long a = long.MinValue;
        long b = -1L;
        dst[t] = (a / b) >> 32;
    }
}
