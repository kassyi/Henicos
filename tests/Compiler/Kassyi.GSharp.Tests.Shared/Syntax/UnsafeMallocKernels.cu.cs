using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class UnsafeMallocKernels
{
    // Each thread allocates its own block, fills it, sums it up and frees it
    [GpuKernel]
    public static unsafe void MallocRoundTripKernel(int[] output)
    {
        int tid = ThreadIndex.X;
#pragma warning disable GSH0029 // The block is freed below
        int* block = (int*)Gpu.Unsafe.Malloc(16);
#pragma warning restore GSH0029
        for (int i = 0; i < 4; i++)
        {
            block[i] = tid * 10 + i;
        }

        int sum = 0;
        for (int i = 0; i < 4; i++)
        {
            sum += block[i];
        }

        output[tid] = sum;
        Gpu.Unsafe.Free(block);
    }
}
