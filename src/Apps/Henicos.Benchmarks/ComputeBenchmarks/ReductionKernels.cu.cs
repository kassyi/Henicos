using System.Runtime.InteropServices;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static class ReductionKernels
{
    [GpuKernel]
    public static void ReductionKernel([In] Float4[] input, [Out] float[] output, int n)
    {
        SharedMemory<float> sdata = new(8);

        int tid = ThreadIndex.X;
        int i = BlockIndex.X * (BlockDimension.X * 2) + tid;

        float sum = 0.0f;
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

        sum += Gpu.ShuffleDownSync(0xFFFFFFFF, sum, 16);
        sum += Gpu.ShuffleDownSync(0xFFFFFFFF, sum, 8);
        sum += Gpu.ShuffleDownSync(0xFFFFFFFF, sum, 4);
        sum += Gpu.ShuffleDownSync(0xFFFFFFFF, sum, 2);
        sum += Gpu.ShuffleDownSync(0xFFFFFFFF, sum, 1);

        int lane = tid % 32;
        int warpId = tid / 32;

        if (lane == 0)
        {
            sdata[warpId] = sum;
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            float val = (tid < 8) ? sdata[tid] : 0.0f;

            val += Gpu.ShuffleDownSync(0xFFFFFFFF, val, 4);
            val += Gpu.ShuffleDownSync(0xFFFFFFFF, val, 2);
            val += Gpu.ShuffleDownSync(0xFFFFFFFF, val, 1);

            if (tid == 0)
            {
                output[BlockIndex.X] = val;
            }
        }
    }
}
