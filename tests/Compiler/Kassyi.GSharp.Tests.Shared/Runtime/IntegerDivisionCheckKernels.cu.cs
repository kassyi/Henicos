#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public static partial class IntegerDivisionCheckKernels
{
    // Each element is written before the next thread runs, so the elements of the threads before a failing one hold
    // their quotients
    [GpuKernel]
    public static void QuotientKernel(int[] a, int[] d, int[] o)
    {
        int i = (int)ThreadIndex.X;
        o[i] = a[i] / d[i];
    }

    // In a device function, which has no error site table, the message names the place of the division
    [GpuKernel]
    public static void DeviceFunctionRemainderKernel(long[] a, long[] d, long[] o)
    {
        int i = (int)ThreadIndex.X;
        o[i] = Remainder(a[i], d[i]);
    }

    [GpuKernel]
    public static void CompoundKernel(uint[] a, uint[] d)
    {
        int i = (int)ThreadIndex.X;
        a[i] /= d[i];
    }

    [GpuDeviceFunction]
    private static long Remainder(long a, long d) => a % d;

    // An int target: int.MinValue /= -1 is the GPU result on every path, also on the managed one
    [GpuKernel]
    public static void SignedCompoundKernel(int[] a, int[] d)
    {
        int i = (int)ThreadIndex.X;
        a[i] /= d[i];
    }
}
