#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[SkipSnapshotConsistency("Verified by ManagedStackBatonTests across wait modes")]
public static partial class ManagedStackBatonKernels
{
    [GpuKernel]
    public static void MultiBarrierKernel(int[] input, int[] output, int n)
    {
        var shared = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        int acc = input[tid];
        for (int round = 0; round < 3; round++)
        {
            shared[tid] = acc;
            Gpu.SyncThreads();
            acc += shared[(tid + round + 1) % n];
            Gpu.SyncThreads();
        }

        output[tid] = acc;
    }

    [GpuKernel]
    public static void WarpDivergenceAndBarrierKernel(int[] input, int[] output, uint[] masks)
    {
        var shared = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        int v = input[tid];
        uint active = 0u;
        if ((tid & 1) == 0)
        {
            active = Warp.ActiveMask();
            v += Warp.ShuffleXorSync(0x55555555u, v, 2);
        }

        shared[tid] = v;
        Gpu.SyncThreads();
        masks[tid] = active;
        output[tid] = shared[tid ^ 1] + (int)(Warp.ActiveMask() & 0xFFu);
    }
}
