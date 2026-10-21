using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class HeavyGpuStressKernels
{
    [GpuKernel]
    public static void MatrixMultiplyStressKernel(float[] a, float[] b, float[] c, int n)
    {
        int row = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;
        int col = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        if (row < n && col < n)
        {
            float sum = 0;
            for (int k = 0; k < n; k++)
            {
                sum += a[row * n + k] * b[k * n + col];
            }
            c[row * n + col] = sum;
        }
    }
}
