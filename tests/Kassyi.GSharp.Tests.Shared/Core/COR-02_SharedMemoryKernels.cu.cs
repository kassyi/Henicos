using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Core;

public class COR_02_SharedMemoryKernels
{
    [GpuKernel]
    public static void ReverseArrayWithSharedMemoryKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;
        if (tid >= length) return;

        // Allocate shared memory (size must be at least block dimension, assume block size = 256)
        var shared = new SharedMemory<int>(256);

        // Load into shared memory
        shared[tid] = input[tid];
        
        Gpu.SyncThreads();

        // Write to output in reverse order
        int reversedIndex = length - 1 - tid;
        output[reversedIndex] = shared[tid];
    }
}
