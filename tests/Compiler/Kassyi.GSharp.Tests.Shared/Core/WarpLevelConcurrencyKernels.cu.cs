using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class WarpLevelConcurrencyKernels
{
    [GpuKernel]
    public static void WarpReduceSumKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int lane = tid % 32;
        int warpId = tid / 32;

        int val = input[tid];

        // Warp synchronous reduction
        uint mask = 0xffffffff;
        val += Warp.ShuffleDownSync(mask, val, 16);
        val += Warp.ShuffleDownSync(mask, val, 8);
        val += Warp.ShuffleDownSync(mask, val, 4);
        val += Warp.ShuffleDownSync(mask, val, 2);
        val += Warp.ShuffleDownSync(mask, val, 1);

        // First thread of each warp writes the result
        if (lane == 0)
        {
            output[warpId] = val;
        }
    }

    [GpuKernel]
    public static void WarpPrimitivesKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        uint mask = Warp.ActiveMask();
        
        int val = input[tid];
        
        // Test all primitives to ensure they emit correct C++ intrinsics
        val = Warp.ShuffleSync(mask, val, 0);
        val = (int)Warp.ShuffleSync(mask, (uint)val, 0);
        val = Warp.ShuffleDownSync(mask, val, 1);
        val = (int)Warp.ShuffleDownSync(mask, (uint)val, 1);
        val = Warp.ShuffleUpSync(mask, val, 1);
        val = Warp.ShuffleXorSync(mask, val, 1);
        
        output[tid] = val;
    }
}
