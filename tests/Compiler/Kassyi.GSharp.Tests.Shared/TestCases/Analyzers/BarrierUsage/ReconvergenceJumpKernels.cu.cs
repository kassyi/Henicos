using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class ReconvergenceJumpKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ForwardGotoIntoGotoLoopMiddleKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if (flag != 0)
        {
            goto middle;
        }

    top:
        result[t] = 1u;
    middle:
        result[t + 32] = Warp.ActiveMask();
        if (n++ < 1)
        {
            goto top;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void BackwardGotoIntoForwardRangeMiddleKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if (flag != 0)
        {
            goto after;
        }

        result[t] = Warp.ActiveMask();
    middle:
        result[t + 32] = 1u;
    after:
        result[t + 64] = 2u;
        if (n++ < 1)
        {
            goto middle;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void CrossSectionGotoIntoForwardRangeMiddleKernel(uint[] result, int branch)
    {
        int t = ThreadIndex.X;
        switch (branch)
        {
            case 0:
                goto middle;
            case 1:
                if ((t & 1) != 0)
                {
                    goto after;
                }

                result[t] = Warp.ActiveMask();
            middle:
                result[t + 32] = Warp.ActiveMask();
            after:
                break;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void BackwardGotoIntoGotoLoopMiddleKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        result[t] = Warp.ActiveMask();
    middle:
        result[t + 32] = 1u;
        if (n++ < 1)
        {
            goto top;
        }

        if (flag != 0 && n == 2)
        {
            goto middle;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void CrossSectionGotoIntoGotoLoopStartKernel(uint[] result, int branch)
    {
        int t = ThreadIndex.X;
        int n = 0;
        switch (branch)
        {
            case 0:
                goto top;
            case 1:
            top:
                result[t] = Warp.ActiveMask();
                if (n++ < 1)
                {
                    goto top;
                }

                break;
        }
    }

    [GpuKernel]
    public static void ForwardGotoIntoForwardRangeMiddleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        if ((t & 2) != 0)
        {
            goto middle;
        }

        if ((t & 1) != 0)
        {
            goto after;
        }

    middle:
        result[t] = Warp.ActiveMask();
    after:
        result[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoToLoopStartFromAfterKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        result[t] = Warp.ActiveMask();
        if (n++ < 1)
        {
            goto top;
        }

        if (n == 2)
        {
            goto top;
        }
    }

    [GpuKernel]
    public static void ForwardGotoToLoopStartKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if (flag != 0)
        {
            goto top;
        }

        result[t] = 1u;
    top:
        result[t + 32] = Warp.ActiveMask();
        if (n++ < 1)
        {
            goto top;
        }
    }

    [GpuKernel]
    public static void GotoIntoLoopMiddleWithoutActiveMaskKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if (flag != 0)
        {
            goto middle;
        }

    top:
        result[t] = 1u;
    middle:
        result[t + 32] = 2u;
        if (n++ < 1)
        {
            goto top;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SeparateEmptyLabelBeforeSplitRejectedKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if (flag != 0)
        {
            goto second;
        }

    first:
        ;
    second:
        result[t] = Warp.ActiveMask();
        if (n++ < 1)
        {
            goto first;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ForUnrollRejectedGotoKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        Gpu.ForUnroll(0, 2, i =>
        {
            int n = 0;
            if (flag != 0)
            {
                goto mid;
            }

        top:
            result[t] = 1u;
        mid:
            result[t + 32 + i] = Warp.ActiveMask();
            if (n++ < 1)
            {
                goto top;
            }
        });
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SyncGridRejectedGotoKernel(uint[] result, int flag)
    {
        int t = ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            {
                int n = 0;
                if (flag != 0)
                {
                    goto mid;
                }

            top:
                result[t] = 1u;
            mid:
                result[t + 32] = Warp.ActiveMask();
                if (n++ < 1)
                {
                    goto top;
                }
            }
        });
    }

    [GpuKernel]
    public static void StackedLabelsOnSplitTwoBackwardGotosKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    first:
    second:
        result[t] = Warp.ActiveMask();
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto first;
        }

        if ((t & 2) == 2 && n < 2)
        {
            goto second;
        }
    }

    [GpuKernel]
    public static void StackedLabelsOnSplitForwardToSecondBackwardToFirstKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if ((t & 1) == 1)
        {
            goto second;
        }

        result[t + 32] = 1u;
    first:
    second:
        result[t] = Warp.ActiveMask();
        if (n++ < 1)
        {
            goto first;
        }
    }
}
