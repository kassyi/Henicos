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

    // Two calls of a device function that reaches a warp collective: each passes the offset of its own frame
    [GpuKernel]
    public static void WarpCallKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int v = HalfThenFull(input[tid], tid & 31);
        output[tid] = HalfThenFull(v, tid & 31) + Warp.ReduceAddSync(0xFFFFFFFFu, tid);
    }

    [GpuDeviceFunction]
    private static int HalfThenFull(int v, int lane)
    {
        if (lane >= 16)
        {
            v += Warp.ShuffleXorSync(0xFFFF0000u, v, 1);
        }

        return v + Warp.ShuffleXorSync(mask: 0xFFFFFFFFu, laneMask: 16, var: v);
    }

    [GpuDeviceFunction]
    private static int WaitAndRead(int value, int tid)
    {
        Gpu.SyncThreads();
        return value + tid * 0;
    }
}
