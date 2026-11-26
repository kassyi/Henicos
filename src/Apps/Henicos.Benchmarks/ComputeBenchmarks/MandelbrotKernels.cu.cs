namespace Henicos.Benchmarks.ComputeBenchmarks;

#pragma warning disable SA1402 // Parameter structs must live in the .cu.cs file for single-file snapshot compilation
public struct MandelbrotDimensions
{
    public int Width;
    public int Height;
    public int MaxIterations;
}

public struct MandelbrotViewport
{
    public float XMin;
    public float XMax;
    public float YMin;
    public float YMax;
}
#pragma warning restore SA1402

public static partial class MandelbrotKernels
{
    [GpuKernel]
    public static void MandelbrotKernel(
        [Out] int[] output, MandelbrotDimensions dimensions, MandelbrotViewport viewport)
    {
        var width = dimensions.Width;
        var height = dimensions.Height;
        var maxIter = dimensions.MaxIterations;
        var xMin = viewport.XMin;
        var xMax = viewport.XMax;
        var yMin = viewport.YMin;
        var yMax = viewport.YMax;

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
