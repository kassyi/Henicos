namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class TransposeKernels
{
    [GpuKernel]
    public static void TransposeKernel([In] float[] input, [Out] float[] output, int width, int height)
    {
        var col = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var row = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;

        if (col < width && row < height)
        {
            output[col * height + row] = input[row * width + col];
        }
    }
}
