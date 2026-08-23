using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.GpuBigInt;

public static class GpuBigIntBenchmarkKernels
{
    [GpuKernel(Cooperative = true)]
    public static void Kernel_BenchmarkSquareChunk(ref FermatContext ctx, int chunkSize)
    {
        for (int iter = 0; iter < chunkSize; iter++)
        {
            GpuBigIntFermatKernels.Device_MultiplySquareAndReduce(3, ref ctx);
        }
    }
}
