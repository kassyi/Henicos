using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Sad Path")]
public static partial class ReconvergenceJumpKernels
{
    [GpuKernel]
    public static void BackwardGotoIntoGotoLoopMiddleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
    top:
        n++;
    mid:
        m = Warp.ActiveMask();
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        if ((t & 2) != 0 && n < 3)
        {
            goto mid;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void ForwardGotoIntoGotoLoopMiddleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        if ((t & 2) != 0)
        {
            goto mid;
        }

    top:
        n++;
    mid:
        m = Warp.ActiveMask();
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void CrossSectionGotoIntoForwardRangeMiddleKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        switch (u)
        {
            case 0:
                if ((t & 1) == 0)
                {
                    goto mid;
                }

                break;
            case 1:
                if ((t & 2) != 0)
                {
                    goto end;
                }

                m = Warp.ActiveMask();
            mid:
                m += Warp.ActiveMask();
            end:
                break;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void BackwardGotoIntoForwardRangeMiddleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        if ((t & 1) != 0)
        {
            goto end;
        }

        m = Warp.ActiveMask();
    mid:
        n++;
    end:
        if (n < 1)
        {
            goto mid;
        }

        result[t] = m;
    }

    [GpuDeviceFunction]
    public static uint JumpIntoMiddleInDeviceFunction(int t)
    {
        int n = 0;
        if ((t & 2) != 0)
        {
            goto mid;
        }

    top:
        n++;
    mid:
        uint m = Warp.ActiveMask();
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        return m;
    }

    [GpuKernel]
    public static void CrossSectionGotoToGotoLoopHeadKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        switch (u)
        {
            case 0:
                if ((t & 1) == 0)
                {
                    goto top;
                }

                break;
            case 1:
            top:
                m = Warp.ActiveMask();
                if ((t & 1) == 1 && n++ < 1)
                {
                    goto top;
                }

                break;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void CrossSectionGotoToChainedLabelGotoLoopHeadKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        switch (u)
        {
            case 0:
                if ((t & 1) == 0)
                {
                    goto second;
                }

                break;
            case 1:
            first:
            second:
                m = Warp.ActiveMask();
                if ((t & 1) == 1 && n++ < 1)
                {
                    goto first;
                }

                break;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void SwitchSelfCycleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        switch (t & 1)
        {
            case 0:
                m = Warp.ActiveMask();
                goto case 0;
            case 1:
                break;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void SwitchMutualCycleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        switch (t & 1)
        {
            case 0:
                m = Warp.ActiveMask();
                goto case 1;
            case 1:
                goto case 0;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void CrossSectionGotoIntoGotoLoopMiddleKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        switch (u)
        {
            case 0:
                if ((t & 1) == 0)
                {
                    goto mid;
                }

                break;
            case 1:
            top:
                n++;
            mid:
                m = Warp.ActiveMask();
                if ((t & 1) == 1 && n < 2)
                {
                    goto top;
                }

                break;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void SwitchBackwardGotoAcrossJoinKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        switch (t % 3)
        {
            case 0:
                m = Warp.ActiveMask();
                goto first;
            case 1:
            first:
                n++;
            second:
                n++;
                if (n < 4)
                {
                    goto first;
                }

                break;
            default:
                goto second;
        }

        result[t] = m;
        result[t + 32] = (uint)n;
        result[t + 64] = Warp.ActiveMask();
    }
}
