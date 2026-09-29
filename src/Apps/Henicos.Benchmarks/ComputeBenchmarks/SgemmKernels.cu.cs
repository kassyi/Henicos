using System.Runtime.InteropServices;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class SgemmKernels
{
    [GpuKernel]
    public static void SgemmKernel([In] float[] a, [In] float[] b, [Out] float[] c, int n)
    {
        SharedMemory<float> As = new(256); // 16 * 16
        SharedMemory<float> Bs = new(256); // 16 * 16

        int bx = BlockIndex.X;
        int by = BlockIndex.Y;
        int tx = ThreadIndex.X;
        int ty = ThreadIndex.Y;

        int row = by * 16 + ty;
        int col = bx * 16 + tx;

        float cValue = 0.0f;

        for (int m = 0; m < (n + 15) / 16; m++)
        {
            if (row < n && m * 16 + tx < n)
            {
                As[ty * 16 + tx] = a[row * n + m * 16 + tx];
            }
            else
            {
                As[ty * 16 + tx] = 0.0f;
            }

            if (m * 16 + ty < n && col < n)
            {
                Bs[ty * 16 + tx] = b[(m * 16 + ty) * n + col];
            }
            else
            {
                Bs[ty * 16 + tx] = 0.0f;
            }

            Gpu.SyncThreads();

            for (int k = 0; k < 16; ++k)
            {
                cValue += As[ty * 16 + k] * Bs[k * 16 + tx];
            }

            Gpu.SyncThreads();
        }

        if (row < n && col < n)
        {
            c[row * n + col] = cValue;
        }
    }
}
