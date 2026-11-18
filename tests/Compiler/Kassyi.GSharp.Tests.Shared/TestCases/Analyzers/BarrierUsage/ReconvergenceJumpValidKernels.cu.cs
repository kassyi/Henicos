using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Analyzer negative test cases")]
public static partial class ReconvergenceJumpValidKernels
{
    [GpuKernel]
    public static void GotoToGotoLoopHeadKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        if ((t & 2) != 0)
        {
            goto top;
        }

    top:
        m = Warp.ActiveMask();
        if ((t & 1) == 1 && n++ < 2)
        {
            goto top;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void GotoIntoMiddleWithoutActiveMaskKernel(uint[] result, uint mask)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if ((t & 2) != 0)
        {
            goto mid;
        }

    top:
        n++;
    mid:
        uint m = Warp.BallotSync(mask, (t & 1) == 0);
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void GotoIntoLoopWithoutSplitPointKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = Warp.ActiveMask();
        if ((t & 2) != 0)
        {
            goto mid;
        }

    top:
        n++;
    mid:
        m += 1u;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void ForwardGotoIntoRangeMiddleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        if ((t & 2) != 0)
        {
            goto mid;
        }

        if ((t & 1) != 0)
        {
            goto end;
        }

    mid:
        m = Warp.ActiveMask();
    end:
        result[t] = m;
    }

    [GpuKernel]
    public static void ChainedLabelSplitWithForwardGotoKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if ((t & 2) != 0)
        {
            goto second;
        }

    first:
    second:
        uint m = Warp.ActiveMask();
        if ((t & 1) == 1 && n++ < 1)
        {
            goto first;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void ChainedLabelDoubleLoopOnSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n1 = 0;
        int n2 = 0;
    first:
    second:
        uint m = Warp.ActiveMask();
        if ((t & 1) == 1 && n1++ < 1)
        {
            goto first;
        }

        if ((t & 2) == 2 && n2++ < 1)
        {
            goto second;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void ChainedLabelConditionalSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        uint m = 0u;
        if ((t & 2) != 0)
        {
            goto second;
        }

    first:
    second:
        if ((t & 1) == 1 && n++ < 1)
        {
            m = Warp.ActiveMask();
            goto first;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void UniformSwitchGotoCaseCycleKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        switch (u)
        {
            case 0:
                if ((t & 1) == 0)
                {
                    m = Warp.ActiveMask();
                    goto case 1;
                }

                break;
            case 1:
                goto case 0;
        }

        result[t] = m;
    }

    [GpuKernel]
    public static void ThreadVariantSwitchCycleWithoutSplitKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint m = Warp.ActiveMask();
        switch (t & 1)
        {
            case 0:
                if (m == 0u)
                {
                    goto case 1;
                }

                break;
            case 1:
                goto case 0;
        }

        result[t] = m;
    }
}
