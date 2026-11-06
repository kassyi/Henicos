using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The attributes are on the defining declarations, which have no body; the implementing declarations are transpiled
public static partial class PartialMethodKernels
{
    [GpuKernel]
    public static partial void PartialFillKernel(int[] data);

    public static partial void PartialFillKernel(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] = SevenPlus(i);
    }

    [GpuDeviceFunction]
    public static partial int SevenPlus(int i);

    public static partial int SevenPlus(int i) => 7 + i;
}
