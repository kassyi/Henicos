#include <cuda_runtime.h>

// Native CUDA reference implementation for tiled SGEMM benchmark
extern "C" __global__ void SgemmKernel_Native(float* a, float* b, float* c, int n)
{
    const int TileSize = 16;
    __shared__ float As[TileSize][TileSize];
    __shared__ float Bs[TileSize][TileSize];

    int bx = blockIdx.x;
    int by = blockIdx.y;
    int tx = threadIdx.x;
    int ty = threadIdx.y;

    int row = by * TileSize + ty;
    int col = bx * TileSize + tx;

    float cValue = 0.0f;

    for (int m = 0; m < (n + TileSize - 1) / TileSize; m++)
    {
        if (row < n && m * TileSize + tx < n)
            As[ty][tx] = a[row * n + m * TileSize + tx];
        else
            As[ty][tx] = 0.0f;

        if (m * TileSize + ty < n && col < n)
            Bs[ty][tx] = b[(m * TileSize + ty) * n + col];
        else
            Bs[ty][tx] = 0.0f;

        __syncthreads();

        for (int k = 0; k < TileSize; ++k)
        {
            cValue += As[ty][k] * Bs[k][tx];
        }

        __syncthreads();
    }

    if (row < n && col < n)
    {
        c[row * n + col] = cValue;
    }
}
