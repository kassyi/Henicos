using Kassyi.GSharp.Core;

namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static class ReductionKernels
{
    [GpuKernel]
    public static void ReductionKernel(float[] input, float[] output, int n)
    {
        // 256 threads per block
        SharedMemory<float> sdata = new SharedMemory<float>(256);

        int tid = ThreadIndex.X;
        int i = BlockIndex.X * (BlockDimension.X * 2) + ThreadIndex.X;

        // Perform first level of reduction: read two elements into shared memory
        float mySum = 0;
        if (i < n)
        {
            mySum = input[i];
        }
        if (i + BlockDimension.X < n)
        {
            mySum += input[i + BlockDimension.X];
        }
        
        sdata[tid] = mySum;
        Gpu.SyncThreads();

        // Perform reduction in shared memory
        for (int s = BlockDimension.X / 2; s > 0; s >>= 1)
        {
            if (tid < s)
            {
                sdata[tid] += sdata[tid + s];
            }
            Gpu.SyncThreads();
        }

        // Write result for this block to global mem
        if (tid == 0)
        {
            output[BlockIndex.X] = sdata[0];
        }
    }
}
