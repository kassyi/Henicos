using Kassyi.GSharp.Core;

namespace TestNs;

public struct DeepBag
{
    public int[] Data;
    public int Tag;
}

public static partial class NullableDeepStructKernels
{
    [GpuKernel]
    public static void NullableDeepBagKernel(DeepBag? optBag, int[] output)
    {
        output[0] = optBag.HasValue ? 1 : 0;
    }
}
