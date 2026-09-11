using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Core;

public class GEN_16_WarpLevelConcurrencyKernels
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
        val += Gpu.ShflDownSync(mask, val, 16);
        val += Gpu.ShflDownSync(mask, val, 8);
        val += Gpu.ShflDownSync(mask, val, 4);
        val += Gpu.ShflDownSync(mask, val, 2);
        val += Gpu.ShflDownSync(mask, val, 1);

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
        uint mask = Gpu.ActiveMask();
        
        int val = input[tid];
        
        // Test all primitives to ensure they emit correct C++ intrinsics
        val = Gpu.ShflSync(mask, val, 0);
        val = (int)Gpu.ShflSync(mask, (uint)val, 0);
        val = Gpu.ShflDownSync(mask, val, 1);
        val = (int)Gpu.ShflDownSync(mask, (uint)val, 1);
        val = Gpu.ShflUpSync(mask, val, 1);
        val = Gpu.ShflXorSync(mask, val, 1);
        
        output[tid] = val;
    }
}
