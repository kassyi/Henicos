using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels whose lanes meet at warp collectives in device functions, loops and divergent branches, after some lanes have
/// exited, and at <see cref="Gpu.SyncWarp"/>; and kernels whose lanes never meet, which CUDA leaves undefined.
/// </summary>
public static partial class WarpRendezvousDifferentialKernels
{
    // A sub-warp shuffle of the upper half before a full-warp shuffle (#1468)
    [GpuDeviceFunction]
    public static int HalfThenFull(int v, int lane)
    {
        if (lane >= 16)
        {
            v += Warp.ShuffleXorSync(0xFFFF0000u, v, 1);
        }

        return v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 16);
    }

    // The same function from two call sites, one after the other
    [GpuDeviceFunction]
    public static int HalfThenFullTwice(int v, int lane)
    {
        int w = HalfThenFull(v, lane);
        return HalfThenFull(w, lane) - w;
    }

    // A shuffle of the lanes of one half of the warp
    [GpuDeviceFunction]
    public static int PairSum(int v, uint mask) => v + Warp.ShuffleXorSync(mask, v, 1);

    // A full-warp shuffle
    [GpuDeviceFunction]
    public static int FullPairSum(int v) => v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);

    [GpuKernel]
    public static void SubWarpShuffleInDeviceFunctionKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        output[t] = HalfThenFull(input[t], t & 31);
    }

    [GpuKernel]
    public static void SubWarpShuffleInNestedDeviceFunctionKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        output[t] = HalfThenFullTwice(input[t], t & 31);
    }

    // The same call site of a device function in every iteration of a loop
    [GpuKernel]
    public static void DeviceFunctionInLoopKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        for (int k = 0; k < 3; k++)
        {
            v = HalfThenFull(v, t & 31) & 0xFFFF;
        }

        output[t] = v;
    }

    // In each iteration, the lanes of one parity shuffle among themselves, then the whole warp shuffles
    [GpuKernel]
    public static void AlternatingDivergenceInLoopKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int lane = t & 31;
        int v = input[t];
        for (int k = 0; k < 4; k++)
        {
            if (((lane + k) & 1) == 0)
            {
                uint mask = (k & 1) == 0 ? 0x55555555u : 0xAAAAAAAAu;
                v += Warp.ShuffleXorSync(mask, v, 2) * (k + 1);
            }

            v = (v + Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1)) & 0xFFFFF;
        }

        output[t] = v;
    }

    // Each half of the warp calls the same device function from its own side of an if/else, with the mask of its half
    [GpuKernel]
    public static void DeviceFunctionOnBothSidesKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        if ((t & 31) < 16)
        {
            v = PairSum(v, 0x0000FFFFu);
        }
        else
        {
            v = PairSum(v, 0xFFFF0000u) * 3;
        }

        output[t] = v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 16);
    }

    // The lanes of the mask that exist in a block whose size is not a multiple of 32
    [GpuKernel]
    public static void PartialWarpVotesKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        int sum = Warp.ReduceAddSync(0xFFFFFFFFu, v);
        uint ballot = Warp.BallotSync(0xFFFFFFFFu, (v & 1) == 0);
        output[t * 2] = sum;
        output[t * 2 + 1] = (int)ballot;
    }

    // Lanes 16..31 exit before the votes: CUDA evaluates them for the lanes of the mask that have not exited
    [GpuKernel]
    public static void VotesAfterExitKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        if ((t & 31) >= 16)
        {
            return;
        }

        int v = input[t];
        output[t * 4] = (int)Warp.BallotSync(0xFFFFFFFFu, (t & 1) == 0);
        output[t * 4 + 1] = Warp.AllSync(0xFFFFFFFFu, (t & 31) < 16) ? 1 : 0;
        output[t * 4 + 2] = Warp.ReduceMinSync(0xFFFFFFFFu, v);
        output[t * 4 + 3] = Warp.ReduceAddSync(0xFFFFFFFFu, v);
    }

    // The lanes of the lower half exchange through shared memory, which Gpu.SyncWarp orders
    [GpuKernel]
    public static void SyncWarpExchangeKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int lane = t & 31;
        var shared = new SharedMemory<int>(64);
        int result = -1;
        if (lane < 16)
        {
            shared[t] = input[t] * 2;
            Gpu.SyncWarp(0x0000FFFFu);
            result = shared[t ^ 1];
        }

        output[t] = result;
    }

    // Each half calls the same full-warp shuffle from its own side of an if/else: the halves never meet (undefined)
    [GpuKernel]
    public static void FullMaskOnBothSidesKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        if ((t & 31) < 16)
        {
            v = FullPairSum(v);
        }
        else
        {
            v = FullPairSum(v) * 3;
        }

        output[t] = v;
    }

    // Two full-warp shuffles, one on each side: the halves never meet (undefined)
    [GpuKernel]
    public static void FullMaskAtTwoSitesKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        if ((t & 31) < 16)
        {
            v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        }
        else
        {
            v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 2);
        }

        output[t] = v;
    }

    // The lower half shuffles with the full mask while the upper half waits at the block barrier (undefined)
    [GpuKernel]
    public static void FullMaskBeforeBarrierKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        if ((t & 31) < 16)
        {
            v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        }

        Gpu.SyncThreads();
        output[t] = v;
    }

    // Lanes 16..31 exit; lane 15 reads lane 16 (undefined)
    [GpuKernel]
    public static void ShuffleReadsExitedLaneKernel(int[] output)
    {
        int t = ThreadIndex.X;
        if ((t & 31) >= 16)
        {
            return;
        }

        output[t] = Warp.ShuffleDownSync(0xFFFFFFFFu, t + 100, 1);
    }
}
