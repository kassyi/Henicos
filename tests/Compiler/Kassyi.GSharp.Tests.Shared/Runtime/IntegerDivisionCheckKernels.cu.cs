#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public struct DivisionCell
{
    public int P { get; set; }
}

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

    // The property of a value type found through its variable: an array element (which may throw) and a local that the
    // value assigns. C# finds the variable once and reads the property before the value, so int.MinValue /= -1 is the
    // GPU result in both, not 1
    [GpuKernel]
    public static void PropertyCompoundKernel(DivisionCell[] cells, int[] d, int[] o)
    {
        cells[0].P /= d[0];
        DivisionCell h = cells[1];
        DivisionCell h2 = cells[2];
        h.P /= (h = h2).P;
        o[0] = h.P;
    }

    // Compound assignments whose value writes something: another variable, the target local, the target element. C#
    // reads the target before the value runs, so int.MinValue /= (-1 written into the target) is the GPU result of
    // int.MinValue / -1, not -1 / -1
    [GpuKernel]
    public static void ValueWritingCompoundKernel(int[] a, int[] d, int[] o)
    {
        int j = 0;
        a[0] /= d[j++];
        int x = a[2];
        x /= (x = d[1]);
        o[0] = x;
        o[1] = j;
        a[1] /= (a[1] = d[2]);
    }

    [GpuKernel]
    public static void StructQuotientKernel(int[] a, int[] d, int[] o)
    {
        int i = (int)ThreadIndex.X;
        o[i] = DivisionHolder.Quotient(a[i], d[i]);
    }
}

// A device function of a partial struct gets its managed version like one of a class (GSH0036 requires partial)
public partial struct DivisionHolder
{
    [GpuDeviceFunction]
    public static int Quotient(int a, int d) => a / d;
}
