#if GSHARP_CUDA
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class CudaStreamKernels
{
    [GpuKernel]
    public static void DelayKernel(int[] data, int iterations)
    {
        var tid = ThreadIndex.X;
        if (tid != 0)
        {
            return;
        }

        // Dummy delay loop to occupy the GPU stream
        var val = 0;
        for (var i = 0; i < iterations; i++)
        {
            val += i;
        }

        data[0] = val;
    }
}
#endif
