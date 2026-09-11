using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Core;

public class GEN_29_SharedMemoryKernels
{
    [GpuKernel]
    public static void ReverseArrayWithSharedMemoryKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;

        // Allocate shared memory (size must be at least block dimension, assume block size = 256)
        var shared = new SharedMemory<int>(256);

        // Load into shared memory
        if (tid < length)
        {
            shared[tid] = input[tid];
        }
        
        Gpu.SyncThreads();

        // Write to output in reverse order
        if (tid < length)
        {
            int reversedIndex = length - 1 - tid;
            output[tid] = shared[reversedIndex];
        }
    }
}
