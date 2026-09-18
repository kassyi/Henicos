namespace Henicos.Benchmarks.Algorithms;

public static class MandelbrotKernels
{
    [GpuKernel]
    public static void MandelbrotKernel(int[] output, int width, int height, int maxIter, float xMin, float xMax, float yMin, float yMax)
    {
        int col = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int row = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;

        if (col < width && row < height)
        {
            float x0 = xMin + (xMax - xMin) * col / width;
            float y0 = yMin + (yMax - yMin) * row / height;
            float x = 0.0f;
            float y = 0.0f;
            int iter = 0;

            while (x * x + y * y <= 4.0f && iter < maxIter)
            {
                float xtemp = x * x - y * y + x0;
                y = 2.0f * x * y + y0;
                x = xtemp;
                iter++;
            }

            output[row * width + col] = iter;
        }
    }
}
