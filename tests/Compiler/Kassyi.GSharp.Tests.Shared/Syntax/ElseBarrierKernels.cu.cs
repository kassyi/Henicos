// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class ElseBarrierKernels
{
    [GpuKernel]
    public static void ReverseWithElseBarrierKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> shared = new(256);
        shared[tid] = input[tid];
        if (length < 0) { output[tid] = -1; }
        else Gpu.SyncThreads();               // non-block else: the bug
        output[tid] = shared[length - 1 - tid];
    }
}
