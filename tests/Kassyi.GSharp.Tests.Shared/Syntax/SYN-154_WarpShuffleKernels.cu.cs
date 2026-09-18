// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_154_WarpShuffleKernels
{
    // [Happy Path] Parallel Reduction Sum using ShuffleDownSync within a 32-thread warp
    [GpuKernel]
    public static void WarpReductionKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int lane = tid % 32;
        int warpId = tid / 32;

        int val = input[tid];

        uint mask = 0xffffffff;
        val += Warp.ShuffleDownSync(mask, val, 16);
        val += Warp.ShuffleDownSync(mask, val, 8);
        val += Warp.ShuffleDownSync(mask, val, 4);
        val += Warp.ShuffleDownSync(mask, val, 2);
        val += Warp.ShuffleDownSync(mask, val, 1);

        if (lane == 0)
        {
            output[warpId] = val;
        }
    }

    // [Edge Cases] Boundary offset values (0, 31, 32)
    [GpuKernel]
    public static void WarpShuffleBoundaryKernel(int[] input, int[] outputBoundary0, int[] outputBoundary31, int[] outputBoundary32)
    {
        int tid = ThreadIndex.X;
        uint mask = 0xffffffff;
        int val = input[tid];

        // Delta = 0: returns thread's own value
        outputBoundary0[tid] = Gpu.ShuffleDownSync(mask, val, 0);

        // Delta = 31: max warp offset
        outputBoundary31[tid] = Gpu.ShuffleDownSync(mask, val, 31);

        // Delta = 32: out-of-bound warp offset
        outputBoundary32[tid] = Gpu.ShuffleDownSync(mask, val, 32);
    }

    // [Edge Cases] Warp Divergence: ShuffleDownSync called within conditional branches
    [GpuKernel]
    public static void WarpDivergenceKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int lane = tid % 32;
        uint mask = 0xffffffff;
        int val = input[tid];

        if (lane < 16)
        {
            mask = 0x0000FFFF;
            val = Gpu.ShuffleDownSync(mask, val, 1, 16);
        }
        else
        {
            mask = 0xFFFF0000;
            val = Gpu.ShuffleDownSync(mask, val, 2, 16);
        }

        output[tid] = val;
    }

    [GpuKernel]
    public static void WarpShuffleExtremaKernel(uint mask, int[] input, uint delta, int width, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = Gpu.ShuffleDownSync(mask, input[tid], delta, width);
    }
}
