using Kassyi.GSharp.Core;

namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class SgemmKernels
{
    [GpuKernel]
    public static void SgemmKernel(float[] a, float[] b, float[] c, int n)
    {
        int row = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;
        int col = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        if (row < n && col < n)
        {
            float cValue = 0.0f;
            for (int k = 0; k < n; k++)
            {
                cValue += a[row * n + k] * b[k * n + col];
            }

            c[row * n + col] = cValue;
        }
    }
}
