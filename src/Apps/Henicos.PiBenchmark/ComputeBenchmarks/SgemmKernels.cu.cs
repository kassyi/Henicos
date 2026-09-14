using Kassyi.GSharp.Core;

namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class SgemmKernels
{
    private const int TileSize = 16;

    [GpuKernel]
    public static void SgemmKernel(float[] a, float[] b, float[] c, int n)
    {
        // 256 threads per block (16x16)
        SharedMemory<float> As = new SharedMemory<float>(TileSize * TileSize);
        SharedMemory<float> Bs = new SharedMemory<float>(TileSize * TileSize);

        int bx = BlockIndex.X;
        int by = BlockIndex.Y;
        int tx = ThreadIndex.X;
        int ty = ThreadIndex.Y;

        int row = by * TileSize + ty;
        int col = bx * TileSize + tx;

        float cValue = 0.0f;

        // Loop over all tiles
        for (int m = 0; m < (n + TileSize - 1) / TileSize; m++)
        {
            // Load tile of A into shared memory
            if (row < n && m * TileSize + tx < n)
            {
                As[ty * TileSize + tx] = a[row * n + m * TileSize + tx];
            }
            else
            {
                As[ty * TileSize + tx] = 0.0f;
            }

            // Load tile of B into shared memory
            if (m * TileSize + ty < n && col < n)
            {
                Bs[ty * TileSize + tx] = b[(m * TileSize + ty) * n + col];
            }
            else
            {
                Bs[ty * TileSize + tx] = 0.0f;
            }

            Gpu.SyncThreads();

            // Multiply the two tiles together
            for (int k = 0; k < TileSize; ++k)
            {
                cValue += As[ty * TileSize + k] * Bs[k * TileSize + tx];
            }

            Gpu.SyncThreads();
        }

        // Write the result to C
        if (row < n && col < n)
        {
            c[row * n + col] = cValue;
        }
    }
}
