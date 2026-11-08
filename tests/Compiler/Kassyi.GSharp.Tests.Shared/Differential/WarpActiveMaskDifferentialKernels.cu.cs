using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels that read <see cref="Warp.ActiveMask"/> after thread-dependent branches, loops and early exits, from device
/// functions called on both sides of a branch, as the mask of a warp collective, and in the last round of
/// <see cref="Gpu.GridStrideLoop(int, System.Action{int})"/> (#844).
/// </summary>
public static partial class WarpActiveMaskDifferentialKernels
{
    // Warp.ActiveMask() of the caller
    [GpuDeviceFunction]
    public static uint MaskOf() => Warp.ActiveMask();

    // The lanes that call it together, and a shuffle among them
    [GpuDeviceFunction]
    public static int PairWithinMask(int v, uint[] masks, int t)
    {
        uint mask = Warp.ActiveMask();
        masks[t] = mask;
        return v + Warp.ShuffleXorSync(mask, v, 1);
    }

    // The lanes that return early from a device function meet the others after the call
    [GpuDeviceFunction]
    public static uint MaskOrReturn(int t, uint[] early)
    {
        if (t < 4)
        {
            early[t] = Warp.ActiveMask();
            return 1u;
        }

        return Warp.ActiveMask();
    }

    [GpuKernel]
    public static void InBranchKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            result[t] = Warp.ActiveMask();
        }
    }

    [GpuKernel]
    public static void AfterEarlyReturnKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        if (t >= 4)
        {
            return;
        }

        result[t] = Warp.ActiveMask();
    }

    // The same device function from both sides of an if/else: two instances of the call
    [GpuKernel]
    public static void DeviceFunctionOnBothSidesKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            result[t] = MaskOf();
        }
        else
        {
            result[t] = MaskOf();
        }
    }

    // A call on each side of an if/else
    [GpuKernel]
    public static void BothSidesKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint mask;
        if ((t & 1) == 0)
        {
            mask = Warp.ActiveMask();
        }
        else
        {
            mask = Warp.ActiveMask();
        }

        result[t] = mask;
    }

    // A branch without a split point keeps the warp together: every lane calls it together after it
    [GpuKernel]
    public static void AfterBranchKernel(uint[] side, uint[] result)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            side[t] = 1u;
        }

        result[t] = Warp.ActiveMask();
    }

    // Lane t runs (t % 4) + 1 iterations
    [GpuKernel]
    public static void DivergentLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        for (int k = 0; k < (t % 4) + 1; k++)
        {
            result[t * 4 + k] = Warp.ActiveMask();
        }
    }

    // Every lane has left a loop of a thread-dependent count before the call
    [GpuKernel]
    public static void AfterDivergentLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint sum = 0u;
        for (int k = 0; k < t % 4; k++)
        {
            sum += (uint)k + 1u;
        }

        result[t * 2] = Warp.ActiveMask();
        result[t * 2 + 1] = sum;
    }

    // The lanes of one parity take the branch in each iteration of a loop that every lane runs
    [GpuKernel]
    public static void AlternatingBranchInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        for (int k = 0; k < 2; k++)
        {
            if (((t + k) & 1) == 0)
            {
                result[t * 2 + k] = Warp.ActiveMask();
            }
        }
    }

    // A loop of a thread-dependent count inside a loop that every lane runs: the inner calls never meet across the outer
    // iterations
    [GpuKernel]
    public static void NestedLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        for (int o = 0; o < 2; o++)
        {
            for (int k = 0; k < (t % 4) + o; k++)
            {
                result[t * 8 + o * 4 + k] = Warp.ActiveMask();
            }
        }
    }

    // The lanes that continue meet the others at the end of the body
    [GpuKernel]
    public static void ContinueInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        for (int k = 0; k < 3; k++)
        {
            if (((t >> k) & 1) == 0)
            {
                result[t * 6 + k * 2] = Warp.ActiveMask();
                continue;
            }

            result[t * 6 + k * 2 + 1] = Warp.ActiveMask();
        }
    }

    // A loop that each lane leaves by a break
    [GpuKernel]
    public static void BreakInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int k = 0;
        while (true)
        {
            if (k >= t % 4)
            {
                break;
            }

            result[t * 4 + k] = Warp.ActiveMask();
            k++;
        }

        result[t * 4 + 3] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void SwitchKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        switch (t % 3)
        {
            case 0:
                result[t] = Warp.ActiveMask();
                break;
            case 1:
                result[t] = Warp.ActiveMask() | 0u;
                break;
            default:
                result[t] = Warp.ActiveMask() ^ 0u;
                break;
        }

        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void EarlyReturnFromDeviceFunctionKernel(uint[] early, uint[] result)
    {
        int t = ThreadIndex.X;
        uint late = MaskOrReturn(t, early);
        result[t] = late;
        result[t + 32] = Warp.ActiveMask();
    }

    // A branch on data
    [GpuKernel]
    public static void DataBranchKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        if (data[t] > 0)
        {
            result[t] = Warp.ActiveMask();
        }
    }

    // The mask of a vote
    [GpuKernel]
    public static void BallotWithActiveMaskKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            result[t] = Warp.BallotSync(Warp.ActiveMask(), (t & 1) == 0);
        }
    }

    [GpuKernel]
    public static void AfterSyncWarpKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            Gpu.SyncWarp(0xFu);
            result[t] = Warp.ActiveMask();
        }
    }

    // Each half of the warp calls the same device function from its own side of an if/else
    [GpuKernel]
    public static void HalvesKernel(int[] input, uint[] masks, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        if (t < 16)
        {
            v = PairWithinMask(v, masks, t);
        }
        else
        {
            v = PairWithinMask(v, masks, t) * 3;
        }

        output[t] = v;
    }

    [GpuKernel]
    public static void GridStrideLoopKernel(uint[] result, int n)
    {
        Gpu.GridStrideLoop(n, i => result[i] = Warp.ActiveMask());
    }

    // The reduction of GpuBigIntCmpKernels.IsZeroKernel, with a mask from Warp.ActiveMask(): each lane writes its value
    [GpuKernel]
    public static void GridStrideReduceKernel(uint[] input, uint[] result, int n)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint mask = Warp.ActiveMask();
            result[i] = Warp.ReduceOrSync(mask, input[i]);
        });
    }

    // The shuffles of GpuBigIntCmpKernels.IsZeroKernel: in the last round, lane 0 reads lane 16, which has left the loop
    // and exited (CUDA leaves the value undefined)
    [GpuKernel]
    public static void GridStrideShuffleTailKernel(uint[] input, uint[] result, int n)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint notZero = input[i] != 0u ? 1u : 0u;
            uint mask = Warp.ActiveMask();
            for (uint offset = 16u; offset > 0u; offset /= 2u)
            {
                notZero |= Warp.ShuffleDownSync(mask, notZero, offset);
            }

            result[i] = notZero;
        });
    }

    [GpuKernel]
    public static void PartialWarpKernel(uint[] result)
    {
        result[ThreadIndex.X] = Warp.ActiveMask();
    }

    // The lower half reads the upper half with the mask of the lower half (CUDA leaves the value undefined)
    [GpuKernel]
    public static void ShuffleOutsideActiveMaskKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int v = t;
        if (t < 16)
        {
            uint mask = Warp.ActiveMask();
            v = Warp.ShuffleXorSync(mask, v, 16);
        }

        output[t] = v;
    }
}
