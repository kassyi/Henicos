namespace Henicos.Benchmarks.Microbenchmarks;

public static class TransposeKernels
{
    [GpuKernel]
    public static void TransposeKernel(float[] input, float[] output, int width, int height)
    {
        int col = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int row = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;

        if (col < width && row < height)
        {
            output[col * height + row] = input[row * width + col];
        }
    }
}
