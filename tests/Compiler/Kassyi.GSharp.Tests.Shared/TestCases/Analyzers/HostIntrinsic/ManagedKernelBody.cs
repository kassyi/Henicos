using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.HostIntrinsic;

// The generator's managed body of this kernel reads the GPU-only members inside a managed launch
public static partial class ManagedKernelBodyKernels
{
    [GpuKernel]
    public static void IndexKernel(int[] output)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        output[i] = i;
        Gpu.SyncThreads();
    }
}
