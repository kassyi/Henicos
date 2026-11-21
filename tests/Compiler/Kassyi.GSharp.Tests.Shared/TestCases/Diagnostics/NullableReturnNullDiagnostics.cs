using Kassyi.GSharp.Core;

namespace TestNs;

public static partial class NullableReturnNullKernels
{
    [GpuKernel]
    public static int? ReturnNullKernel(int[] output)
    {
        output[0] = 0;
        return null;
    }
}
