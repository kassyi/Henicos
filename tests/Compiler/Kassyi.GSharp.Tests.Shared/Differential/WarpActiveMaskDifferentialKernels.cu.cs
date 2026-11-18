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
}
