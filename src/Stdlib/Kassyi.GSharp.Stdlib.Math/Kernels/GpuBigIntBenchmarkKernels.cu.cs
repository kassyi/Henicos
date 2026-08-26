// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.Kernels;

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

