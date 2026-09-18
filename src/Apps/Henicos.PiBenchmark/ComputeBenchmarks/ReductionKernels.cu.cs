namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class ReductionKernels
{
    [GpuKernel]
    public static void ReductionKernel(float[] input, float[] output, int n)
    {
        int bx = BlockIndex.X;
        int threadsPerBlock = BlockDimension.X;
        int elementsPerBlock = threadsPerBlock * 2;
        int start = bx * elementsPerBlock;
        int end = start + elementsPerBlock;
        if (end > n)
        {
            end = n;
        }

        if (ThreadIndex.X == 0)
        {
            float blockSum = 0.0f;
            for (int i = start; i < end; i++)
            {
                blockSum += input[i];
            }

            output[bx] = blockSum;
        }
    }
}
