using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class CopyAsyncKernels
{
    [GpuKernel]
    public static void CopyAsyncIntKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        var smem = new SharedMemory<int>(32);
        Gpu.CopyAsync(ref smem[tid], in input[tid]);
        Gpu.CopyAsyncCommitGroup();
        Gpu.CopyAsyncWaitGroup(0);
        Gpu.SyncThreads();
        output[tid] = smem[31 - tid];
    }

    [GpuKernel]
    public static void CopyAsyncVector4Kernel(Vector4[] input, Vector4[] output)
    {
        int tid = ThreadIndex.X;
        var smem = new SharedMemory<System.Numerics.Vector4>(32);
        Gpu.CopyAsync(ref smem[tid], in input[tid]);
        Gpu.CopyAsyncCommitGroup();
        Gpu.CopyAsyncWaitGroup(0);
        Gpu.SyncThreads();
        output[tid] = smem[(tid + 1) % 32];
    }

    [GpuKernel]
    public static void CopyAsyncTwoGroupsKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        var smem = new SharedMemory<int>(32);
        if (tid < 16)
        {
            Gpu.CopyAsync(ref smem[tid], in input[tid]);
        }

        Gpu.CopyAsyncCommitGroup();

        if (tid < 16)
        {
            Gpu.CopyAsync(ref smem[tid + 16], in input[tid + 16]);
        }

        Gpu.CopyAsyncCommitGroup();

        Gpu.CopyAsyncWaitGroup(1);
        Gpu.SyncThreads();
        if (tid < 16)
        {
            output[tid] = smem[15 - tid];
        }

        Gpu.CopyAsyncWaitAll();
        Gpu.SyncThreads();
        if (tid >= 16)
        {
            output[tid] = smem[47 - tid];
        }
    }
}
