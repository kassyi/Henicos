using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class GridSyncManagedKernels
{
    [GpuKernel(Cooperative = true)]
    public static void GlobalBarrierKernel(int[] buffer)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int totalThreads = GridDimension.X * BlockDimension.X;

        // Every thread writes its ID + 1 to the buffer
        buffer[tid] = tid + 1;

        // Wait for all blocks to finish writing
        Gpu.SyncGrid();

        // Every thread reads the value of the (tid + 1) % totalThreads element
        // Without SyncGrid, this could read uninitialized data
        int nextId = (tid + 1) % totalThreads;
        int nextVal = buffer[nextId];

        Gpu.SyncGrid();

        // Write the result back (so we can assert it from the host)
        // We write nextVal to buffer[tid]
        buffer[tid] = nextVal;
    }

    [GpuKernel(Cooperative = true)]
    public static void GlobalReductionKernel(int[] input, int[] blockSums, int[] totalSum)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        // 1. Thread-local sum
        int myVal = input[tid];

        // 2. Block-local reduction (using shared memory)
        var shared = new SharedMemory<int>(256); // Assuming block size 256
        int localTid = ThreadIndex.X;
        shared[localTid] = myVal;

        Gpu.SyncThreads();

        // simple sum reduction
        for (int stride = BlockDimension.X / 2; stride > 0; stride >>= 1)
        {
            if (localTid < stride)
            {
                shared[localTid] += shared[localTid + stride];
            }

            Gpu.SyncThreads();
        }

        // 3. Block 0 of each grid writes to blockSums
        if (localTid == 0)
        {
            blockSums[BlockIndex.X] = shared[0];
        }

        // Wait for all blocks to finish writing to blockSums
        Gpu.SyncGrid();

        // 4. Block 0 does the final reduction
        if (BlockIndex.X == 0 && localTid == 0)
        {
            int sum = 0;
            for (int i = 0; i < GridDimension.X; i++)
            {
                sum += blockSums[i];
            }

            totalSum[0] = sum;
        }
    }
}
