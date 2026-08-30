using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class DynamicStringInlinePtxKernel
{
    [GpuKernel]
    public static void Kernel(int[] data)
    {
        int tid = ThreadIndex.X;
        string dynamicPtx = $"data[0] = {tid};";
        Gpu.InlinePtx(dynamicPtx);
    }
}
