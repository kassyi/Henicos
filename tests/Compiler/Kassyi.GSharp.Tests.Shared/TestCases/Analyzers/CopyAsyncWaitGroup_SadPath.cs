using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static partial class CopyAsyncWaitGroupSadPathKernels
{
    [GpuDeviceFunction]
    private static void LoadFromInParam(SharedMemory<int> smem, int tid, in int src)
    {
        Gpu.CopyAsync(ref smem[tid], in src);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void InvalidWaitGroupsKernel(int[] input, int[] output, int dynamicCount)
    {
        var smem = new SharedMemory<int>(32);
        var smem2 = new SharedMemory<int>(32);
        int tid = ThreadIndex.X;
        Gpu.CopyAsync(ref smem[tid], in input[tid]);
        Gpu.CopyAsync(ref smem[tid], input[tid]);
        LoadFromInParam(smem, tid, in input[tid]);
        Gpu.CopyAsyncCommitGroup();
        Gpu.CopyAsyncWaitGroup(0);
        Gpu.CopyAsyncWaitGroup(7);
        Gpu.CopyAsyncWaitGroup(dynamicCount);
        Gpu.CopyAsyncWaitGroup(-1);
        Gpu.CopyAsyncWaitGroup(8);

        short s = 1;
        int local = 0;
        int x = input[tid];
        Gpu.CopyAsync(ref smem[tid], input[tid] + 1);
        Gpu.CopyAsync(ref smem[tid], s);
        Gpu.CopyAsync(ref smem[tid], in x);
        Gpu.CopyAsync(ref smem[tid], in smem2[tid]);
        Gpu.CopyAsync(ref local, in input[tid]);
        Gpu.CopyAsync(ref output[tid], in input[tid]);

        output[tid] = smem[tid] + smem2[tid] + local + x;
    }
}
