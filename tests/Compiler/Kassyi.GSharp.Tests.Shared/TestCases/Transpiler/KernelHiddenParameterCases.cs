using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace KernelHiddenParameterCases;

// The inputs of KernelHiddenParametersTests: a kernel that returns nothing, one that returns a value, and a
// cooperative one whose AsParallel() sum uses the grid strategy (a scratch parameter)
public static partial class Kernels
{
    [GpuKernel]
    public static void VoidKernel(int[] data)
    {
        data[0] = 1;
    }

    [GpuKernel]
    public static int ValueKernel(int[] data)
    {
        return data[0];
    }

    [GpuKernel(Cooperative = true)]
    public static void GridSumKernel(long[] data)
    {
        data[0] = data.AsStackEnumerable().AsParallel().Sum();
    }
}
