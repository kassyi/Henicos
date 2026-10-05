using System.Runtime.InteropServices;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class ReductionKernels
{
    [GpuKernel]
    public static void ReductionKernel([In] Float4[] input, [Out] float[] output, int n)
    {
        SharedMemory<float> sdata = new(8);

        var tid = ThreadIndex.X;
        var i = BlockIndex.X * (BlockDimension.X * 2) + tid;

        var sum = 0.0f;
        if (i < n)
        {
            var v = input[i];
            sum = v.X + v.Y + v.Z + v.W;
        }
        if (i + BlockDimension.X < n)
        {
            var v = input[i + BlockDimension.X];
            sum += v.X + v.Y + v.Z + v.W;
        }

        sum += Warp.ShuffleDownSync(0xFFFFFFFF, sum, 16);
        sum += Warp.ShuffleDownSync(0xFFFFFFFF, sum, 8);
        sum += Warp.ShuffleDownSync(0xFFFFFFFF, sum, 4);
        sum += Warp.ShuffleDownSync(0xFFFFFFFF, sum, 2);
        sum += Warp.ShuffleDownSync(0xFFFFFFFF, sum, 1);

        var lane = tid % 32;
        var warpId = tid / 32;

        if (lane == 0)
        {
            sdata[warpId] = sum;
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            var val = (tid < 8) ? sdata[tid] : 0.0f;

            val += Warp.ShuffleDownSync(0xFFFFFFFF, val, 4);
            val += Warp.ShuffleDownSync(0xFFFFFFFF, val, 2);
            val += Warp.ShuffleDownSync(0xFFFFFFFF, val, 1);

            if (tid == 0)
            {
                output[BlockIndex.X] = val;
            }
        }
    }
}
