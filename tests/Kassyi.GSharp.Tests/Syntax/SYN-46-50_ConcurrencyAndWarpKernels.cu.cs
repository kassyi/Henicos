// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_46_50_ConcurrencyAndWarpKernels
{
    // SYN-47: 共有メモリ (Static / Dynamic)
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

    // SYN-49: アトミック演算 & イントリンシック
    [GpuKernel]
    public static void IntrinsicsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid < 10)
        {
            // Thread 0-9 adds its id to data[0] atomically
            Gpu.AtomicAdd(ref data[0], tid);
        }

        Gpu.SyncThreads();

        if (tid == 0)
        {
            // Just testing math function
            data[1] = System.Math.Max(data[0], 100);
        }
    }

    [GpuKernel]
    public static void AtomicAddStressKernel(int[] globalSum)
    {
        // Every thread in the grid atomically adds 1 to globalSum[0]
        Gpu.AtomicAdd(ref globalSum[0], 1);
    }

    [GpuKernel]
    public static void AtomicOrMaxStressKernel(uint[] globalState)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        
        // globalState[0]: AtomicOr with a unique bit for the first 31 threads
        if (tid < 31)
        {
            Gpu.AtomicOr(ref globalState[0], (uint)(1 << tid));
        }

        // globalState[1]: AtomicMax with the thread ID
        Gpu.AtomicMax(ref globalState[1], (uint)tid);
    }

    // SYN-46: ワープシャッフル & 同期
    [GpuKernel]
    public static void WarpPrimitivesKernel(int[] input, int[] shuffleOut)
    {
        int tid = ThreadIndex.X;
        int val = input[tid];

        Gpu.SyncThreads();

        // Broadcast lane 0 value to all threads in warp
        int shuffled = Gpu.WarpShuffle(val, 0);

        // Shuffle with explicit full mask
        int shuffledExplicit = Gpu.WarpShuffle(0xFFFFFFFF, val, 1);

        shuffleOut[tid] = shuffled * 1000 + shuffledExplicit;
    }
}
