// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class MEM_05_UnmanagedBoundariesKernels
{
    // MEM-05: Safe: Stack Allocation (stackalloc)
    [GpuKernel]
    public static void SafeStackallocKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        unsafe
        {
            int* p = stackalloc int[10];
            for (int i = 0; i < 10; i++)
            {
                p[i] = i * 2;
            }
            output[tid] = p[5];
        }
    }

    // MEM-05: Safe: Memory Pinning (fixed)
    [GpuKernel]
    public static void SafeMemoryPinningKernel(byte[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        unsafe
        {
            fixed (byte* p = &input[0])
            {
                output[tid] = p[tid];
            }
        }
    }
}
