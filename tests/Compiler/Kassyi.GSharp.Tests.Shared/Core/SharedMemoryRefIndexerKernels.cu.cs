using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class SharedMemoryRefIndexerKernels
{
    [GpuKernel]
    public static void AtomicAddSharedMemoryKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var counter = new SharedMemory<int>(1);
        if (tid == 0)
        {
            counter[0] = 0;
        }

        Gpu.SyncThreads();
        Gpu.AtomicAdd(ref counter[0], 1);
        Gpu.SyncThreads();

        if (tid == 0)
        {
            output[0] = counter[0];
        }
    }

    [GpuKernel]
    public static void MBarrierSharedMemoryKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var bar = new SharedMemory<ulong>(1);
        var data = new SharedMemory<int>(32);
        if (tid == 0)
        {
            Gpu.MBarrierInit(ref bar[0], 32);
        }

        Gpu.SyncThreads();
        data[tid] = tid * 3;
        Gpu.MBarrierArrive(ref bar[0]);
        Gpu.MBarrierWait(ref bar[0], 0);
        output[tid] = data[(tid + 1) % 32];
    }
}
