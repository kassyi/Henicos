// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct HoistedPoint
{
    public float X;
    public float Y;
}

public static partial class HoistedFieldTypeKernels
{
    [GpuKernel]
    public static void HoistedVarKernel(int[] data)
    {
        var sum = 0u;
        sum += (uint)data[ThreadIndex.X];
        Gpu.SyncThreads();
        data[ThreadIndex.X] = (int)sum;
    }

    [GpuKernel]
    public static void FloatWarpReductionKernel(float[] input, float[] output)
    {
        int tid = ThreadIndex.X;
        int lane = tid % 32;
        int warpId = tid / 32;

        float val = input[tid];

        val += Warp.ShuffleDownSync(0xffffffff, val, 16);
        val += Warp.ShuffleDownSync(0xffffffff, val, 8);
        val += Warp.ShuffleDownSync(0xffffffff, val, 4);
        val += Warp.ShuffleDownSync(0xffffffff, val, 2);
        val += Warp.ShuffleDownSync(0xffffffff, val, 1);

        if (lane == 0)
        {
            output[warpId] = val;
        }
    }

    [GpuKernel]
    public static void StructVarKernel(HoistedPoint[] items, float[] output)
    {
        int tid = ThreadIndex.X;
        var p = items[tid];
        Gpu.SyncThreads();
        output[tid] = p.X + p.Y;
    }
}
