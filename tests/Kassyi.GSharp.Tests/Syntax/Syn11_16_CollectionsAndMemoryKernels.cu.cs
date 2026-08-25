using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class Syn11_16_CollectionsAndMemoryKernels
{
    [GpuKernel]
    public static void SharedMemoryKernel(int[] data, int dynSize)
    {
        int tid = ThreadIndex.X;

        // Static Shared Memory
        SharedMemory<int> sStatic = new SharedMemory<int>(256);

        // Dynamic Shared Memory
        SharedMemory<int> sDyn1 = new SharedMemory<int>(dynSize);
        SharedMemory<float> sDyn2 = new SharedMemory<float>(dynSize);

        sStatic[tid] = data[tid];
        sDyn1[tid] = data[tid] * 2;
        sDyn2[tid] = 3.14f;

        Gpu.SyncThreads();

        data[tid] = sStatic[tid] + sDyn1[tid];
    }

    [GpuKernel]
    public static void SharedMemoryReverseKernel(int[] data, int size)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> sMem = new SharedMemory<int>(256);

        if (tid < size)
        {
            sMem[tid] = data[tid];
        }

        Gpu.SyncThreads();

        if (tid < size)
        {
            // Reverse the array across all warps
            data[tid] = sMem[size - 1 - tid];
        }
    }
}
