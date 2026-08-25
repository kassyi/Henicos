using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class Syn57_93_ModernPatternsKernels
{
    [GpuDevice]
    public static int MyCustomAdd(int a, int b)
    {
        return a + b;
    }

    [GpuKernel]
    public static void DeviceMethodKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = MyCustomAdd(data[tid], 100);
    }
}
