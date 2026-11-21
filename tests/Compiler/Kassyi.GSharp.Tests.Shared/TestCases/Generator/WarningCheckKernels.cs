#nullable enable
using Kassyi.GSharp.Core;
namespace TestNs;
public partial class WarningCheckKernels
{
    [GpuDeviceFunction]
    public static int SyncHelper(int x)
    {
        Gpu.SyncThreads();
        if (false)
        {
            return -1;
        }
        return x + 1;
    }

    [GpuKernel]
    public static void TestKernel(int[]? values, int[] output)
    {
        int unused = 0;
        var f = float.NaN;
        if (f != f && values != null)
        {
            output[0] = SyncHelper(values[0]);
        }
    }
}
