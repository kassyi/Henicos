using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cpu.Benchmarks;

[SkipSnapshotConsistency("Micro-benchmark kernels verified by BarrierModelBenchmarkTests")]
public static partial class BarrierModelBenchmarkKernels
{
    [GpuKernel]
    public static void NoBarrierKernel(int[] data, int[] output, int rounds, int work)
    {
        int t = ThreadIndex.X + (BlockIndex.X * BlockDimension.X);
        int acc = 0;
        for (int i = 0; i < rounds; i++)
        {
            acc += data[ThreadIndex.X] + i;
            for (int k = 0; k < work; k++)
            {
                acc = (acc * 3) + k;
            }
        }

        output[t] = acc;
    }

    [GpuKernel]
    public static void BarrierLoopKernel(int[] data, int[] output, int rounds, int work)
    {
        int t = ThreadIndex.X + (BlockIndex.X * BlockDimension.X);
        int acc = 0;
        for (int i = 0; i < rounds; i++)
        {
            acc += data[ThreadIndex.X] + i;
            for (int k = 0; k < work; k++)
            {
                acc = (acc * 3) + k;
            }

            Gpu.SyncThreads();
        }

        output[t] = acc;
    }

    [GpuKernel]
    public static void ShuffleLoopKernel(int[] data, int[] output, int rounds, int work)
    {
        int t = ThreadIndex.X + (BlockIndex.X * BlockDimension.X);
        int acc = data[ThreadIndex.X];
        for (int i = 0; i < rounds; i++)
        {
            acc += Warp.ShuffleXorSync(0xFFFFFFFFu, acc, 1) & 0xFF;
            for (int k = 0; k < work; k++)
            {
                acc = (acc * 3) + k;
            }
        }

        output[t] = acc;
    }

    [GpuKernel]
    public static void ActiveMaskLoopKernel(int[] data, int[] output, int rounds, int work)
    {
        int t = ThreadIndex.X + (BlockIndex.X * BlockDimension.X);
        int acc = data[ThreadIndex.X];
        for (int i = 0; i < rounds; i++)
        {
            if ((ThreadIndex.X & 1) == 0)
            {
                acc += (int)(Warp.ActiveMask() & 0xFFu);
            }

            for (int k = 0; k < work; k++)
            {
                acc = (acc * 3) + k;
            }
        }

        output[t] = acc;
    }
}
