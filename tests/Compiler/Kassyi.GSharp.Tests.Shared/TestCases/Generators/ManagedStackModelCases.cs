using Kassyi.GSharp.Core;

namespace ManagedStackModelGeneratorCases;

[SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
public static partial class StackModelKernels
{
    [GpuKernel]
    public static void ReverseKernel(int[] input, int[] output, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        output[tid] = WaitAndRead(s[n - 1 - tid], tid);
    }

    [GpuKernel]
    public static void ShuffleKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        output[tid] = Warp.ShuffleXorSync(0xFFFFFFFFu, input[tid], 1);
    }

    [GpuDeviceFunction]
    private static int WaitAndRead(int value, int tid)
    {
        Gpu.SyncThreads();
        return value + tid * 0;
    }
}
