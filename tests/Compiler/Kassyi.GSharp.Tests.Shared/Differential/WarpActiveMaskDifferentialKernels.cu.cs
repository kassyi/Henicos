using System.Collections.Generic;
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

    // A uniform switch with a nested thread-dependent break
    [GpuKernel]
    public static void UniformSwitchBreakKernel(uint[] result, int uniformCond)
    {
        int t = ThreadIndex.X;
        switch (uniformCond)
        {
            case 0:
                result[t] = Warp.ActiveMask();
                if ((t & 1) == 1)
                {
                    break;
                }

                result[t + 32] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        result[t + 64] = Warp.ActiveMask();
    }

    // A uniform switch where a nested branch jumps to another case with differing split point counts
    [GpuKernel]
    public static void UniformSwitchGotoCaseKernel(uint[] result, int uniformCond)
    {
        int t = ThreadIndex.X;
        switch (uniformCond)
        {
            case 0:
                result[t] = Warp.ActiveMask();
                if ((t & 1) == 1)
                {
                    goto case 1;
                }

                result[t + 32] = Warp.ActiveMask();
                result[t + 64] = Warp.ActiveMask();
                break;
            case 1:
                result[t + 96] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        result[t + 128] = Warp.ActiveMask();
    }

    // A uniform switch with a labeled break targeting the switch from inside an inner loop
    [GpuKernel]
    public static void UniformSwitchLabeledBreakKernel(uint[] result, int uniformCond)
    {
        int t = ThreadIndex.X;
        sw: switch (uniformCond)
        {
            case 0:
                result[t] = Warp.ActiveMask();
                for (int i = 0; i < 2; i++)
                {
                    if ((t & 1) == 1)
                    {
                        break sw;
                    }
                }

                result[t + 32] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        result[t + 64] = Warp.ActiveMask();
    }

    // A uniform switch with a nested break followed by a warp shuffle across all lanes
    [GpuKernel]
    public static void UniformSwitchBreakShuffleKernel(int[] output, int uniformCond)
    {
        int t = ThreadIndex.X;
        int v = t;
        switch (uniformCond)
        {
            case 0:
                uint m1 = Warp.ActiveMask();
                if ((t & 1) == 1)
                {
                    v += (int)(m1 & 0u);
                    break;
                }

                uint m2 = Warp.ActiveMask();
                v += (int)(m2 & 0u);
                break;
            default:
                break;
        }

        output[t] = Warp.ShuffleXorSync(Warp.ActiveMask(), v, 1);
    }

    [GpuKernel]
    public static void ForwardGotoKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m1 = Warp.ActiveMask();
        uint m2 = 0u;
        if ((t & 1) == 1)
        {
            goto after;
        }

        m2 = Warp.ActiveMask();
    after:
        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoOutOfUniformIfKernel(uint[] result, int uniformCond)
    {
        int t = ThreadIndex.X;
        uint m1 = Warp.ActiveMask();
        uint m2 = 0u;
        if (uniformCond == 0)
        {
            if ((t & 1) == 1)
            {
                goto after;
            }

            m2 = Warp.ActiveMask();
        }

    after:
        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoFromBranchWithSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        if ((t & 1) == 1)
        {
            m1 = Warp.ActiveMask();
            goto after;
        }

        m2 = Warp.ActiveMask();
    after:
        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void TwoGotosOneLabelKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        if ((t & 3) == 1)
        {
            goto done;
        }

        m1 = Warp.ActiveMask();
        if ((t & 3) == 2)
        {
            goto done;
        }

        m2 = Warp.ActiveMask();
    done:
        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CrossingGotosKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint x = 0u;
        uint y = 0u;
        uint z = 0u;
        if ((t & 1) == 1)
        {
            goto fail;
        }

        x = Warp.ActiveMask();
        if ((t & 2) == 2)
        {
            goto done;
        }

        y = Warp.ActiveMask();
    fail:
        z = Warp.ActiveMask();
    done:
        result[t] = x;
        result[t + 32] = y;
        result[t + 64] = z;
        result[t + 96] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoOutOfLoopInRangeKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m1 = Warp.ActiveMask();
        uint m2 = 0u;
        for (int i = 0; i < 2; i++)
        {
            if ((t & 1) == 1)
            {
                goto after;
            }

            m2 = Warp.ActiveMask();
        }

    after:
        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoToOtherSectionKernel(uint[] result, int uniformCond)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        uint m3 = 0u;
        uint m4 = 0u;
        if ((t & 4) == uniformCond)
        {
            switch (t & 1)
            {
                case 0:
                    m1 = Warp.ActiveMask();
                    goto shared;
                case 1:
                    m2 = Warp.ActiveMask();
                shared:
                    m3 = Warp.ActiveMask();
                    break;
            }

            m4 = Warp.ActiveMask();
        }

        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = m3;
        result[t + 96] = m4;
        result[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ForwardGotoShuffleKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int v = t;
        uint m1 = Warp.ActiveMask();
        if ((t & 1) == 1)
        {
            v += (int)(m1 & 0u);
            goto after;
        }

        uint m2 = Warp.ActiveMask();
        v += (int)(m2 & 0u);
    after:
        output[t] = Warp.ShuffleXorSync(Warp.ActiveMask(), v, 1);
    }

    [GpuKernel]
    public static void GotoBackToRangeStartKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    start:
        if ((t & 1) == 1)
        {
            goto after;
        }

        m = Warp.ActiveMask();
        if (n++ < 1)
        {
            goto start;
        }

    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    // The jumped-over block has more statements than the index of the label in the outer list: the join stays in the
    // outer list, so the lanes that jump skip the whole block
    [GpuKernel]
    public static void ForwardGotoOverNestedBlockKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        if ((t & 1) == 1)
        {
            goto after;
        }

        {
            result[t + 64] = 1u;
            result[t + 64] = 2u;
            result[t + 64] = 3u;
            result[t + 64] = 4u;
            m = Warp.ActiveMask();
        }

    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    // The range starts with a block that holds a label and a backward goto to it: the goto stays inside the range
    [GpuKernel]
    public static void GotoInsideFirstStatementOfRangeKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        {
        again:
            if ((t & 1) == 1)
            {
                goto after;
            }

            m |= Warp.ActiveMask();
            if (n++ < 1)
            {
                goto again;
            }
        }

    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    // The statement that opens the range carries two labels; both stay in front of the region enter
    [GpuKernel]
    public static void ForwardGotoFromDoublyLabeledStatementKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
    first:
    second:
        if ((t & 1) == 1)
        {
            goto after;
        }

        m = Warp.ActiveMask();
    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    // A forward goto from before a range to a label in its middle: its own range holds no split point, but the lanes
    // that take it must still enter the outer range before they land inside it
    [GpuKernel]
    public static void ForwardGotoIntoRangeMiddleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        if ((t & 2) != 0)
        {
            goto middle;
        }

        if ((t & 1) != 0)
        {
            goto after;
        }

    middle:
        m = Warp.ActiveMask();
    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void BackwardGotoKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    top:
        m = Warp.ActiveMask();
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopWithExitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    top:
        m = Warp.ActiveMask();
        if ((t & 2) != 0)
        {
            goto after;
        }

        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void TwoBackwardGotosKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        uint m1 = Warp.ActiveMask();
        if ((t & 2) == 2 && n < 2)
        {
            goto top;
        }

        result[t] = m1;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void DoublyLabeledGotoLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    first:
    second:
        if ((t & 1) == 1)
        {
            goto after;
        }

        m = Warp.ActiveMask();
    after:
        if ((t & 2) == 0 && n++ < 1)
        {
            goto second;
        }

        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopExitAfterSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    top:
        m = Warp.ActiveMask();
        n++;
        if ((t & 4) != 0)
        {
            goto after;
        }

        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ForwardGotoToLoopHeadKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint mSkipped = 0u;
        uint mLoop = 0u;
        if ((t & 2) != 0)
        {
            goto top;
        }

        mSkipped = 1u;
    top:
        mLoop = Warp.ActiveMask();
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        result[t] = mSkipped;
        result[t + 32] = mLoop;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInsideLabeledStatementKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    top:
        if ((t & 1) == 1 && n++ < 1)
        {
            m = Warp.ActiveMask();
            goto top;
        }

        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopAtKernelEndKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        result[t + (n * 32)] = Warp.ActiveMask();
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            result[t + 64] = Warp.ActiveMask();
            goto top;
        }
    }

    [GpuKernel]
    public static void NestedGotoLoopsKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int outerN = 0;
        uint mInner = 0u;
        uint mAfterInner = 0u;
    outer:
        int innerN = 0;
    inner:
        mInner = Warp.ActiveMask();
        innerN++;
        if ((t & 1) == 1 && innerN < 2)
        {
            goto inner;
        }

        mAfterInner = Warp.ActiveMask();
        outerN++;
        if (outerN < 2)
        {
            goto outer;
        }

        result[t] = mInner;
        result[t + 32] = mAfterInner;
        result[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoBackToRangeStartWithoutSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    X:
        if ((t & 1) == 1)
        {
            goto M;
        }

        if (n++ < 16)
        {
            goto X;
        }

        m = Warp.ActiveMask();
    M:
        result[t] = m;
    }

    [GpuKernel]
    public static void GotoLoopOverNestedBlockKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    top:
        n++;
        {
            result[t + 64] = 1u;
            result[t + 64] = 2u;
            result[t + 64] = 3u;
            result[t + 64] = 4u;
            result[t + 64] = 5u;
            m = Warp.ActiveMask();
        }

        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ChainedLabelDoubleLoopOnSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n1 = 0;
        int n2 = 0;
    L0:
    L1:
        uint m = Warp.ActiveMask();
        if ((t & 1) == 1 && n1++ < 1)
        {
            goto L0;
        }

        if ((t & 2) == 2 && n2++ < 1)
        {
            goto L1;
        }

        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ChainedLabelSplitWithForwardGotoKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint skipped = 0u;
        if ((t & 2) != 0)
        {
            goto L1;
        }

        skipped = 1u;
    L0:
    L1:
        uint m = Warp.ActiveMask();
        if ((t & 1) == 1 && n++ < 1)
        {
            goto L0;
        }

        result[t] = skipped;
        result[t + 32] = m;
        result[t + 64] = Warp.ActiveMask();
    }

    // An inner branch and post-branch ActiveMask called from a divergent outer branch across loop iterations
    [GpuDeviceFunction]
    public static uint InnerMaskInBranch(int t, uint[] result)
    {
        if (t == 2)
        {
            result[t * 2] = Warp.ActiveMask();
        }

        return Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ReenteredBranchInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint a = 0u;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                if (t == 2)
                {
                    a = Warp.ActiveMask();
                }

                b = Warp.ActiveMask();
            }
        }

        result[t * 2] = a;
        result[t * 2 + 1] = b;
    }

    [GpuKernel]
    public static void ReenteredDeviceFunctionInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                b = InnerMaskInBranch(t, result);
            }
        }

        result[t * 2 + 1] = b;
    }

    [GpuKernel]
    public static void ReenteredSwitchInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint a = 0u;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                switch (t)
                {
                    case 2:
                        a = Warp.ActiveMask();
                        break;
                    default:
                        break;
                }

                b = Warp.ActiveMask();
            }
        }

        result[t * 2] = a;
        result[t * 2 + 1] = b;
    }

    [GpuKernel]
    public static void ReenteredInnerLoopInLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint a = 0u;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                int n = t == 2 ? 2 : 0;
                for (int k = 0; k < n; k++)
                {
                    a = Warp.ActiveMask();
                }

                b = Warp.ActiveMask();
            }
        }

        result[t * 2] = a;
        result[t * 2 + 1] = b;
    }

    [GpuKernel]
    public static void ReenteredBranchWithEarlyReturnKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint a = 0u;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                if (t == 2)
                {
                    a = Warp.ActiveMask();
                }

                if (t == 6)
                {
                    result[t * 2 + 1] = 0xDEADu;
                    return;
                }

                b = Warp.ActiveMask();
            }
        }

        result[t * 2] = a;
        result[t * 2 + 1] = b;
    }

    [GpuKernel]
    public static void ReenteredBranchWithForwardGotoKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint a = 0u;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                if (t != 2)
                {
                    goto skip;
                }

                a = Warp.ActiveMask();
            skip:
                b = Warp.ActiveMask();
            }
        }

        result[t * 2] = a;
        result[t * 2 + 1] = b;
    }

    [GpuKernel]
    public static void RangeRestartWithSplitAfterLabelKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    restart:
        if ((t & 1) == 1)
        {
            goto after;
        }

        if (n++ < 2)
        {
            goto restart;
        }

        m = Warp.ActiveMask();
    after:
        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopReenteredFromBranchKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint a = 0u;
        uint b = 0u;
    top:
        n++;
        if ((t & 1) == 1)
        {
            a = Warp.ActiveMask();
            if (n < 2)
            {
                goto top;
            }
        }

        b = Warp.ActiveMask();
        if (t == 100)
        {
            goto top;
        }

        result[t] = a;
        result[t + 32] = b;
    }

    [GpuKernel]
    public static void ThreeLevelReenteredBranchKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint a = 0u;
        uint b = 0u;
        for (int i = 0; i < 2; i++)
        {
            if (((t >> i) & 1) == 1)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (((t >> (2 + j)) & 1) == 1)
                    {
                        if (t == 14)
                        {
                            a = Warp.ActiveMask();
                        }

                        b = Warp.ActiveMask();
                    }
                }
            }
        }

        result[t * 2] = a;
        result[t * 2 + 1] = b;
    }

    // Thread-dependent switch with goto case into a section other lanes entered directly
    [GpuKernel]
    public static void GotoCaseJoinKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        uint m3 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m1 = Warp.ActiveMask();
                m2 = Warp.ActiveMask();
                goto case 1;
            case 1:
                m3 = Warp.ActiveMask();
                break;
        }

        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = m3;
        result[t + 96] = Warp.ActiveMask();
    }

    // Thread-dependent switch with goto case into an earlier section
    [GpuKernel]
    public static void GotoCaseToEarlierSectionKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        uint m3 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m3 = Warp.ActiveMask();
                break;
            case 1:
                m1 = Warp.ActiveMask();
                m2 = Warp.ActiveMask();
                goto case 0;
        }

        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = m3;
        result[t + 96] = Warp.ActiveMask();
    }

    // Thread-dependent switch with cross-section goto to a label after another split point
    [GpuKernel]
    public static void GotoLabelJoinKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        uint m3 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m1 = Warp.ActiveMask();
                m1 = Warp.ActiveMask();
                goto shared;
            case 1:
                m2 = Warp.ActiveMask();
            shared:
                m3 = Warp.ActiveMask();
                break;
        }

        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = m3;
        result[t + 96] = Warp.ActiveMask();
    }

    // Thread-dependent switch with goto default into default section
    [GpuKernel]
    public static void GotoDefaultJoinKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        uint m3 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m1 = Warp.ActiveMask();
                m2 = Warp.ActiveMask();
                goto default;
            default:
                m3 = Warp.ActiveMask();
                break;
        }

        result[t] = m1;
        result[t + 32] = m2;
        result[t + 64] = m3;
        result[t + 96] = Warp.ActiveMask();
    }

    // Three sections chained via goto case with differing split point counts
    [GpuKernel]
    public static void GotoCaseChainKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m0a = 0u;
        uint m0b = 0u;
        uint m1 = 0u;
        uint m2 = 0u;
        switch ((t % 3) + u)
        {
            case 0:
                m0a = Warp.ActiveMask();
                m0b = Warp.ActiveMask();
                goto case 1;
            case 1:
                m1 = Warp.ActiveMask();
                goto case 2;
            case 2:
                m2 = Warp.ActiveMask();
                break;
        }

        result[t] = m0a;
        result[t + 32] = m0b;
        result[t + 64] = m1;
        result[t + 96] = m2;
        result[t + 128] = Warp.ActiveMask();
    }

    // Two sections both jumping via goto case to the same target section
    [GpuKernel]
    public static void TwoSourcesOneJoinKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m0 = 0u;
        uint m1 = 0u;
        uint m2a = 0u;
        uint m2b = 0u;
        switch ((t % 3) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                goto case 1;
            case 1:
                m1 = Warp.ActiveMask();
                break;
            case 2:
                m2a = Warp.ActiveMask();
                m2b = Warp.ActiveMask();
                goto case 1;
        }

        result[t] = m0;
        result[t + 32] = m1;
        result[t + 64] = m2a;
        result[t + 96] = m2b;
        result[t + 128] = Warp.ActiveMask();
    }

    // Conditional goto case leaving some lanes to break directly
    [GpuKernel]
    public static void ConditionalGotoCaseKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m0 = 0u;
        uint m0After = 0u;
        uint m1 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                if ((t & 2) != 0)
                {
                    goto case 1;
                }

                m0After = Warp.ActiveMask();
                break;
            case 1:
                m1 = Warp.ActiveMask();
                break;
        }

        result[t] = m0;
        result[t + 32] = m0After;
        result[t + 64] = m1;
        result[t + 96] = Warp.ActiveMask();
    }

    // Warp shuffle at the join of a thread-dependent switch
    [GpuKernel]
    public static void GotoCaseJoinShuffleKernel(int[] output, int u)
    {
        int t = ThreadIndex.X;
        int v = t;
        switch ((t & 1) + u)
        {
            case 0:
                uint m1 = Warp.ActiveMask();
                uint m2 = Warp.ActiveMask();
                v += (int)((m1 ^ m2) & 0u);
                goto case 1;
            case 1:
                uint m3 = Warp.ActiveMask();
                output[t] = Warp.ShuffleXorSync(m3, v, 1);
                break;
        }
    }

    // Section 0 conditionally jumps to case 1 and falls through to goto case 2 (skips join 1)
    [GpuKernel]
    public static void SkippedJoinGotoCaseKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m0 = 0u;
        uint m0b = 0u;
        uint m1 = 0u;
        uint m2 = 0u;
        switch ((t % 3) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                if ((t & 1) == 0)
                {
                    goto case 1;
                }

                m0b = Warp.ActiveMask();
                goto case 2;
            case 1:
                m1 = Warp.ActiveMask();
                break;
            case 2:
                m2 = Warp.ActiveMask();
                break;
        }

        result[t] = m0;
        result[t + 32] = m0b;
        result[t + 64] = m1;
        result[t + 96] = m2;
        result[t + 128] = Warp.ActiveMask();
    }

    // Thread-dependent switch whose join label is also the target of a backward goto inside its own section: the lanes
    // that loop back pass the label again but not the join, which lies in front of it
    [GpuKernel]
    public static void BackwardGotoToJoinLabelKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m0 = 0u;
        uint m1 = 0u;
        uint m2 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                goto again;
            case 1:
                m1 = Warp.ActiveMask();
            again:
                n++;
                if (n < 2)
                {
                    goto again;
                }

                m2 = Warp.ActiveMask();
                break;
        }

        result[t] = m0;
        result[t + 32] = m1;
        result[t + 64] = m2;
        result[t + 96] = (uint)n;
        result[t + 128] = Warp.ActiveMask();
    }

    // Thread-dependent switch whose forward-goto label is also a join of lanes from another section, while the lanes of
    // the forward goto still hold the join of an earlier section that a goto case of theirs reaches
    [GpuKernel]
    public static void ForwardGotoToSwitchJoinLabelKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m0 = 0u;
        uint m1 = 0u;
        uint m2 = 0u;
        switch ((t % 3) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                break;
            case 1:
                if ((t & 4) != 0)
                {
                    goto shared;
                }

                if ((t & 8) != 0)
                {
                    goto case 0;
                }

                m1 = Warp.ActiveMask();
            shared:
                m2 = Warp.ActiveMask();
                break;
            default:
                goto shared;
        }

        result[t] = m0;
        result[t + 32] = m1;
        result[t + 64] = m2;
        result[t + 96] = Warp.ActiveMask();
    }

    // The same switch in a device function
    [GpuKernel]
    public static void ForwardGotoToSwitchJoinLabelInDeviceFunctionKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        SwitchJoinAtForwardGotoLabel(result, t, u);
        result[t + 96] = Warp.ActiveMask();
    }

    [GpuDeviceFunction]
    public static void SwitchJoinAtForwardGotoLabel(uint[] result, int t, int u)
    {
        uint m0 = 0u;
        uint m1 = 0u;
        uint m2 = 0u;
        switch ((t % 3) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                break;
            case 1:
                if ((t & 4) != 0)
                {
                    goto shared;
                }

                if ((t & 8) != 0)
                {
                    goto case 0;
                }

                m1 = Warp.ActiveMask();
            shared:
                m2 = Warp.ActiveMask();
                break;
            default:
                goto shared;
        }

        result[t] = m0;
        result[t + 32] = m1;
        result[t + 64] = m2;
    }

    // Thread-dependent switch with a goto case to its own section: a retry loop back to the section head, no join
    [GpuKernel]
    public static void SelfGotoCaseRetryKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m0 = 0u;
        uint m1 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m0 = Warp.ActiveMask();
                n++;
                if (n < 1 + ((t >> 1) & 1))
                {
                    goto case 0;
                }

                break;
            default:
                m1 = Warp.ActiveMask();
                break;
        }

        result[t] = m0;
        result[t + 32] = m1;
        result[t + 64] = (uint)n;
        result[t + 96] = Warp.ActiveMask();
    }

    // A goto loop whose last statement is a block: the loop ends right after the if that holds its last goto back to
    // L1, inside the block, so the statement after that if runs with every lane. The lanes with (t & 3) != 0 take the
    // goto twice
    [GpuKernel]
    public static void GotoLoopEndingInBlockKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // GotoLoopEndingInBlockKernel with the if in a block nested in the last statement: the loop ends right after the if,
    // two blocks deep
    [GpuKernel]
    public static void GotoLoopEndingInNestedBlockKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        {
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
            }

            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // Two gotos back to L1 in the last block: the loop ends after the second, so the lanes do not meet at the statement
    // between them, only after the second. The lanes with (t & 3) == 1 take the first goto once, those with
    // (t & 3) == 2 the second
    [GpuKernel]
    public static void GotoLoopEndingInBlockWithTwoGotosKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        {
            if ((t & 3) == 1 && n++ < 1)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
            if ((t & 3) == 2 && n++ < 1)
            {
                goto L1;
            }

            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop whose last backward goto is inside an if without else: lanes reconverge right after the goto inside
    // the if, then again after the if
    [GpuKernel]
    public static void GotoLoopEndingInIfKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // A goto loop whose last backward goto is inside the then-branch of an if-else: lanes reconverge right after the
    // goto in the then-branch while the else-branch runs on its own, then all lanes meet after the if-else
    [GpuKernel]
    public static void GotoLoopEndingInIfElseKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }
        else
        {
            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop whose last backward goto is inside two nested ifs: lanes reconverge at each enclosing level as they
    // leave the loop
    [GpuKernel]
    public static void GotoLoopEndingInNestedIfKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 8) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
            }

            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop whose last backward goto is inside the middle branch of an if / else if / else: lanes reconverge
    // right after the goto in that branch while the other branches run their own lanes
    [GpuKernel]
    public static void GotoLoopEndingInElseIfKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 12) == 0)
        {
            result[t + 96] = Warp.ActiveMask();
        }
        else if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 128] = Warp.ActiveMask();
        }
        else
        {
            result[t + 160] = Warp.ActiveMask();
        }

        result[t + 192] = Warp.ActiveMask();
        result[t + 224] = (uint)n;
    }

    // Nested goto loops where the inner loop ends inside an if while the outer loop continues: the inner loop
    // reconverges inside the if on every outer pass without closing the outer loop
    [GpuKernel]
    public static void NestedGotoLoopsEndingInIfKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int a = 0;
        int b = 0;
    L0:
        result[t + 32 * a] = Warp.ActiveMask();
        b = 0;
    L1:
        result[t + 32 * (2 + 2 * a + b)] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 1) != 0 && b < 1)
            {
                b++;
                goto L1;
            }

            result[t + 32 * (6 + a)] = Warp.ActiveMask();
        }

        if ((t & 2) != 0 && a < 1)
        {
            a++;
            goto L0;
        }

        result[t + 256] = Warp.ActiveMask();
        result[t + 288] = (uint)a;
    }

    // A goto loop ending inside an if in a device function, called with thread-dependent parameter t
    [GpuKernel]
    public static void GotoLoopEndingInIfInDeviceFunctionKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        GotoInIf(result, t);
        result[t + 160] = Warp.ActiveMask();
    }

    [GpuDeviceFunction]
    public static void GotoInIf(uint[] result, int t)
    {
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }

        result[t + 128] = Warp.ActiveMask();
    }

    // A goto loop whose lanes leave the loop inside an if via a collective and a forward goto out of the loop:
    // lanes reconverge before the forward goto, then all lanes meet at the target label
    [GpuKernel]
    public static void GotoLoopEndingInIfThenForwardGotoKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
            goto done;
        }

        n += 8;
    done:
        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // A goto loop that some lanes leave in different passes through an early-exit block before a forward goto:
    // each class runs its exit block in its own pass, while the remaining lanes reconverge after the backward goto
    [GpuKernel]
    public static void GotoLoopLeftByForwardGotoBlockKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int p = 0;
    L1:
        result[t + 32 * p] = Warp.ActiveMask();
        if ((t & 4) == 0 && p == (t & 3))
        {
            result[t + 128] = Warp.ActiveMask();
            goto done;
        }

        if (p < 3)
        {
            p++;
            goto L1;
        }

        result[t + 160] = Warp.ActiveMask();
    done:
        result[t + 192] = Warp.ActiveMask();
    }

    // Two backward gotos in the label's list with a mask at the header and between the gotos: lanes that return by
    // either goto meet at L1 on the next pass. Slots: 0,1 header per pass; 2,3 between gotos per pass; 4 after; 5 n
    [GpuKernel]
    public static void TwoBackwardGotosWithHeaderMaskKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int p = -1;
    L1:
        p++;
        result[t + 32 * p] = Warp.ActiveMask();
        if ((t & 3) == 1 && n++ < 1)
        {
            goto L1;
        }

        result[t + 32 * (2 + p)] = Warp.ActiveMask();
        if ((t & 3) == 2 && n++ < 1)
        {
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // Two backward gotos with the pass-limit operand first in both conditions, so on the second pass the lanes that
    // returned by either goto fail the same operand. Slots: 0,1 header; 2,3 between gotos; 4 after; 5 n
    [GpuKernel]
    public static void TwoBackwardGotosSameOperandFirstKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int p = -1;
    L1:
        p++;
        result[t + 32 * p] = Warp.ActiveMask();
        if (n < 1 && (t & 3) == 1)
        {
            n++;
            goto L1;
        }

        result[t + 32 * (2 + p)] = Warp.ActiveMask();
        if (n < 1 && (t & 3) == 2)
        {
            n++;
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // One backward goto in each branch of an if-else after a different number of calls: returning lanes meet at L1
    // on the second pass, and exiting lanes reconcile before the post-branch call. Slots: 0,1 header; 2 then; 3 else; 4 after; 5 p
    [GpuKernel]
    public static void BackwardGotosInBothBranchesKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int p = -1;
    L1:
        p++;
        result[t + 32 * p] = Warp.ActiveMask();
        if ((t & 1) == 0)
        {
            result[t + 64] = Warp.ActiveMask();
            if ((t & 3) == 0 && p < 1)
            {
                goto L1;
            }
        }
        else
        {
            if ((t & 3) == 1 && p < 1)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)p;
    }

    // Two backward gotos with a collective inside the first goto's branch and a run-time trip limit u. Slots: 0 inside
    // first goto; 1,2 between gotos per pass; 3 after; 4 n
    [GpuKernel]
    public static void TwoBackwardGotosWithMaskInFirstGotoKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        n++;
        if ((t & 1) == 1 && n < u + 2)
        {
            result[t] = Warp.ActiveMask();
            goto top;
        }

        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 2) == 2 && n < u + 2)
        {
            goto top;
        }

        result[t + 96] = Warp.ActiveMask();
        result[t + 128] = (uint)n;
    }

    // Two backward gotos with a side-effecting store before the first goto so the GPU compiler preserves the first
    // loop pass structure. Slots: 0,1 between gotos; 2 after; 3 n; 4,5 stores per pass
    [GpuKernel]
    public static void TwoBackwardGotosWithStoreKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        n++;
        result[t + 32 * (3 + n)] = (uint)(n + u);
        if ((t & 1) == 1 && n < u + 2)
        {
            goto top;
        }

        result[t + 32 * (n - 1)] = Warp.ActiveMask();
        if ((t & 2) == 2 && n < u + 2)
        {
            goto top;
        }

        result[t + 64] = Warp.ActiveMask();
        result[t + 96] = (uint)n;
    }

    // GotoLoopReenteredFromBranchKernel with a run-time trip limit u and separate slots per pass so pass separation
    // is directly observable. Slots: 0,1 inner branch per pass; 2,3 after inner branch per pass; 4 after loop
    [GpuKernel]
    public static void GotoLoopReenteredFromBranchPerPassKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        n++;
        if ((t & 1) == 1)
        {
            result[t + 32 * (n - 1)] = Warp.ActiveMask();
            if (n < u + 2)
            {
                goto top;
            }
        }

        result[t + 32 * (n + 1)] = Warp.ActiveMask();
        if (t == u + 100)
        {
            goto top;
        }

        result[t + 128] = Warp.ActiveMask();
    }

    // Forward goto from before a chained label group into its second label, followed by two backward gotos targeting
    // different labels of the group. Slots: 0,1 header per pass; 2,3 between gotos per pass; 4 after; 5,6 (n + dummy)
    [GpuKernel]
    public static void ChainedHeaderForwardEntryTwoBackwardGotosKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int dummy = 0;
        if ((t & 16) == 0)
        {
            goto L1;
        }

        dummy = 1;
    L0:
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        n++;
        result[t + 32 * (4 + n)] = (uint)(n + dummy);
        if ((t & 1) == 1 && n < 2)
        {
            goto L0;
        }

        result[t + 32 * (1 + n)] = Warp.ActiveMask();
        if ((t & 2) == 2 && n < 2)
        {
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
    }

    // One backward goto inside a run-time for loop, taken in different loop iterations by different lane classes.
    // Slots: 0,1 header per pass; 2 + 2p + i loop body; 6 after; 7 p
    [GpuKernel]
    public static void GotoLoopThroughForLoopKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int p = -1;
    L1:
        p++;
        result[t + 32 * p] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if (p < 1 && (t & 3) == i + 1)
            {
                goto L1;
            }

            result[t + 32 * (2 + 2 * p + i)] = Warp.ActiveMask();
        }

        result[t + 192] = Warp.ActiveMask();
        result[t + 224] = (uint)p;
    }

    // One backward goto inside a run-time for loop where returning lanes take the goto on the first iteration.
    // Slots: 0..2 header; 3,4 iterations; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopThroughForLoopSamePassKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 32 * (3 + i)] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop whose backward goto is inside a do-while(false) loop: constant false folds away the back edge, so
    // the statement after the goto inside the do body is a gate where all lanes reconverge. Slots: 0..2 header;
    // 3 after goto inside do body; 4 after; 5 n
    [GpuKernel]
    public static void GotoLoopThroughConstantFalseDoLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        do
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        }
        while (false);

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // A goto loop simulated via a flag and break out of a run-time for loop followed by a backward goto after it.
    // Slots: 0..2 header; 3,4 iterations; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopThroughFlagAndBreakKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        bool again = false;
        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                again = true;
                break;
            }

            result[t + 32 * (3 + i)] = Warp.ActiveMask();
        }

        if (again)
        {
            goto L1;
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A backward goto inside a constant-trip-count for loop whose condition only depends on the counter. Slots: 0..2
    // header; 3,4 iterations; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopThroughConstantForLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < 2; i++)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 32 * (3 + i)] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A run-time trip count with a backward goto whose condition is invariant across iterations of the for loop.
    // Slots: 0..2 header; 3..6 iterations; 7 after; 8 n
    [GpuKernel]
    public static void GotoLoopThroughLoopWithFixedConditionKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < u + 4; i++)
        {
            if ((t & 3) != 0 && n < 2)
            {
                n++;
                goto L1;
            }

            result[t + 32 * (3 + i)] = Warp.ActiveMask();
        }

        result[t + 224] = Warp.ActiveMask();
        result[t + 256] = (uint)n;
    }

    // Two backward gotos with the same condition and no state change between them. Slots: 0..2 header; 3 between the
    // gotos; 4 after; 5 n
    [GpuKernel]
    public static void GotoLoopWithRepeatedConditionKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 3) != 0 && n < 2)
        {
            n++;
            goto L1;
        }

        result[t + 96] = Warp.ActiveMask();
        if ((t & 3) != 0 && n < 2)
        {
            n++;
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // A constant-trip-count loop where each lane class breaks in a different iteration after reading its mask inside the
    // break block. Slots: 0..3 per iteration; 4 break block; 5 after
    [GpuKernel]
    public static void BreakBlockInConstantLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        for (int i = 0; i < 4; i++)
        {
            result[t + 32 * i] = Warp.ActiveMask();
            if (i == (t & 3))
            {
                result[t + 128] = Warp.ActiveMask();
                break;
            }
        }

        result[t + 160] = Warp.ActiveMask();
    }

    // A goto loop whose gate statement lies inside an enclosing for loop before a break: lanes that leave via the gate
    // wait there for both the returning lanes and the lanes that exit the for loop without entering the if. Slots: 0..2
    // header; 3 gate inside for loop; 4 else-path inside for loop; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopLeftInsideForLoopKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
                break;
            }

            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop with an early-return gate inside a run-time for loop and a second backward goto after the loop:
    // the for loop's normal exit remains in the goto loop, so the goto loop closes at the early-return gate and after
    // the trailing goto rather than at the for loop's exit. Slots: 0..2 header per pass; 3 gate before return; 4
    // between for and second goto; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopWithEarlyReturnInForAndSecondGotoKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int j = 0; j < u + 2; j++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
                return;
            }
        }

        result[t + 128] = Warp.ActiveMask();
        if ((t & 8) == 0 && n++ < 2)
        {
            goto L1;
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop wrapping nested for loops where the inner for contains an early-return gate and the outer for
    // holds a second backward goto followed by break: the inner for's normal exit remains in the goto loop, while the
    // outer for closes the goto loop at its break and normal exit. Slots: 0..2 header per pass; 3 gate before return
    // in inner for; 4 between loops in outer for; 5 after outer for; 6 n
    [GpuKernel]
    public static void GotoLoopWithEarlyReturnInNestedForKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            for (int j = 0; j < u + 2; j++)
            {
                if ((t & 4) == 0)
                {
                    if ((t & 3) != 0 && n++ < 2)
                    {
                        goto L1;
                    }

                    result[t + 96] = Warp.ActiveMask();
                    return;
                }
            }

            result[t + 128] = Warp.ActiveMask();
            if ((t & 8) == 0 && n++ < 2)
            {
                goto L1;
            }

            if (u >= 0)
            {
                break;
            }
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop wrapping a for loop where the gate is preceded by a continue statement in the same block: the
    // continue branch stays in the goto loop, so the statement after it is still a gate. Slots: 0..2 header per pass;
    // 3 gate before break; 4 else-path inside for loop; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopWithContinueBeforeGateInForKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                if (i == 0)
                {
                    continue;
                }

                result[t + 96] = Warp.ActiveMask();
                break;
            }

            result[t + 128] = Warp.ActiveMask();
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop wrapping nested for loops where a labeled break exits the outer loop into a trailing backward goto
    // while an early-return gate sits right after it in the inner loop: the inner loop closes the goto loop, while
    // the outer loop keeps the goto loop open across its exit. Slots: 0..2 header per pass; 3 gate before return in
    // inner for; 4 outer-else before return; 5 after; 6 n
    [GpuKernel]
    public static void GotoLoopWithLabeledBreakIntoLoopInNestedForKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
    outer:
        for (int i = 0; i < u + 2; i++)
        {
            for (int j = 0; j < u + 2; j++)
            {
                if ((t & 4) == 0)
                {
                    if ((t & 3) != 0 && n++ < 2)
                    {
                        goto L1;
                    }

                    if ((t & 8) == 0)
                    {
                        break outer;
                    }

                    result[t + 96] = Warp.ActiveMask();
                    return;
                }
            }

            result[t + 128] = Warp.ActiveMask();
            return;
        }

        if ((t & 8) == 0 && n++ < 1)
        {
            goto L1;
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // Nested goto loops wrapping a for loop that exits only the inner goto loop while an early-return gate follows the
    // inner backward goto inside the for loop: the gate closes both nested goto loops. Slots: 0..2 header per pass; 3
    // gate before return; 4 between for and trailing outer goto; 5 after; 6 n
    [GpuKernel]
    public static void NestedGotoLoopsWithForExitingInnerLoopBeforeGateKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L0:
        result[t + 32 * n] = Warp.ActiveMask();
    L1:
        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L0;
                }

                if ((t & 2) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
                return;
            }
        }

        result[t + 128] = Warp.ActiveMask();
        if ((t & 8) == 0 && n++ < 1)
        {
            goto L0;
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // Nested goto loops wrapping a for loop with continue and break that exits only the inner goto loop before an
    // early-return gate: break closes the inner goto loop while the gate closes both. Slots: 0..2 header per pass; 3
    // gate before return; 4 between for and trailing outer goto; 5 after; 6 n
    [GpuKernel]
    public static void NestedGotoLoopsWithContinueAndBreakExitingInnerLoopKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L0:
        result[t + 32 * n] = Warp.ActiveMask();
    L1:
        for (int i = 0; i < u + 2; i++)
        {
            if (i == 0)
            {
                continue;
            }

            if ((t & 4) == 0)
            {
                break;
            }

            if ((t & 3) != 0 && n++ < 2)
            {
                goto L0;
            }

            if ((t & 8) == 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
            return;
        }

        result[t + 128] = Warp.ActiveMask();
        if ((t & 16) == 0 && n++ < 1)
        {
            goto L0;
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop where a conditional forward goto to a member label precedes an early-return gate: the forward goto
    // remains in the goto loop, so the statement after it is still a gate. Slots: 0..2 header per pass; 3 gate before
    // return; 4 after; 5 n
    [GpuKernel]
    public static void GotoLoopWithConditionalForwardGotoToMemberBeforeGateKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            if ((t & 8) == 0)
            {
                goto M;
            }

            result[t + 96] = Warp.ActiveMask();
            return;
        }

    M:
        if ((t & 8) == 0 && n++ < 1)
        {
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // Device function holding a goto loop gated inside a for loop with an early return: returning lanes must leave all
    // open callee regions and reconverge with the normal-exit lanes after the call.
    [GpuDeviceFunction]
    public static void GotoInForWithEarlyReturn(uint[] result, int t, int u)
    {
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int j = 0; j < u + 2; j++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                result[t + 96] = Warp.ActiveMask();
                return;
            }
        }

        result[t + 128] = Warp.ActiveMask();
    }

    // Calls GotoInForWithEarlyReturn and observes the full warp mask after the call returns. Slots: 0..2 callee header
    // per pass; 3 callee gate before early return; 4 callee after for; 5 after call
    [GpuKernel]
    public static void GotoLoopWithEarlyReturnInForInDeviceFunctionKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        GotoInForWithEarlyReturn(result, t, u);
        result[t + 160] = Warp.ActiveMask();
    }

    // A goto loop with a conditional forward goto to a member label before a return gate in the same block: the member
    // label after the return must restore the enclosing goto loop and latch regions. Slots: 0..2 header per pass;
    // 3 gate before return; 4 after member; 5 n
    [GpuKernel]
    public static void GotoLoopTopLevelForwardGotoToMemberPastReturnGateKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            result[t + 32 * n] = Warp.ActiveMask();
        }

        if ((t & 3) != 0 && n++ < 2)
        {
            goto L1;
        }

        if ((t & 8) == 0)
        {
            goto M;
        }

        result[t + 96] = Warp.ActiveMask();
        return;
    M:
        if ((t & 4) == 0 && m++ < 1)
        {
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }

    // A goto loop inside a for loop with a conditional forward goto to a member label before a break gate: the member
    // label after the break must keep the goto loop and latch open until break closes the loop. Slots: 0..2 header
    // per pass; 3 gate before break; 4 after member before break; 5 after for; 6 n
    [GpuKernel]
    public static void GotoLoopInForForwardGotoToMemberPastBreakGateKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            result[t + 32 * n] = Warp.ActiveMask();
        }

        for (int i = 0; i < u + 2; i++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                if ((t & 8) == 0)
                {
                    goto M;
                }

                result[t + 96] = Warp.ActiveMask();
                break;
            M:
                if ((t & 1) == 0 && m++ < 1)
                {
                    goto L1;
                }

                result[t + 128] = Warp.ActiveMask();
                break;
            }
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    // A goto loop where a plain block ends with a return gate before a member label in the enclosing list: the member
    // label must restore the enclosing goto loop and latch regions after the non-reaching block. Slots: 0..2 header
    // per pass; 3 gate before return; 4 after member; 5 n
    [GpuKernel]
    public static void GotoLoopPlainBlockForwardGotoToMemberPastReturnGateKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            result[t + 32 * n] = Warp.ActiveMask();
        }

        if ((t & 3) != 0 && n++ < 2)
        {
            goto L1;
        }

        {
            if ((t & 8) == 0)
            {
                goto M;
            }

            result[t + 96] = Warp.ActiveMask();
            return;
        }

    M:
        if ((t & 4) == 0 && m++ < 1)
        {
            goto L1;
        }

        result[t + 128] = Warp.ActiveMask();
        result[t + 160] = (uint)n;
    }
}
