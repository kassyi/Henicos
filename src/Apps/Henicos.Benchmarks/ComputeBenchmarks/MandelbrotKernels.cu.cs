namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class MandelbrotKernels
{
    [GpuKernel]
    public static void MandelbrotKernel(
        [Out] int[] output, int width, int height, int maxIter, float xMin, float xMax, float yMin, float yMax)
    {
        var col = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var row = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;

        if (col < width && row < height)
        {
            var x0 = xMin + (xMax - xMin) * col / width;
            var y0 = yMin + (yMax - yMin) * row / height;
            var x = 0.0f;
            var y = 0.0f;
            int iter = 0;

            while (x * x + y * y <= 4.0f && iter < maxIter)
            {
                var xtemp = x * x - y * y + x0;
                y = 2.0f * x * y + y0;
                x = xtemp;
                iter++;
            }

            output[row * width + col] = iter;
        }
    }
}
