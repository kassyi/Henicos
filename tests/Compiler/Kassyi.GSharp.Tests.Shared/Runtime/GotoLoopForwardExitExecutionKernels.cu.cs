using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[SkipSnapshotConsistency("Tests CPU and CUDA execution stability for unplanned goto loops with forward exits inside the loop range")]
public static partial class GotoLoopForwardExitExecutionKernels
{
    [GpuKernel]
    public static void ForwardGotoBeforeGateInEarlierIfKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 8) == 0)
            {
                goto done;
            }

            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        done:
            result[t + 128] = Warp.ActiveMask();
            goto after;
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void ForwardGotoBeforeGateInDeviceFunctionKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        if ((t & 31) < 16)
        {
            n = ForwardGotoInIfHelper(result, t);
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuDeviceFunction]
    public static int ForwardGotoInIfHelper(uint[] result, int t)
    {
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 8) == 0)
            {
                goto done;
            }

            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            result[t + 96] = Warp.ActiveMask();
        done:
            result[t + 128] = Warp.ActiveMask();
            return n;
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

        return n;
    }

    [GpuKernel]
    public static void ForwardGotoToOuterLabelBeforeLastKernel(uint[] result)
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
            if ((t & 8) == 0)
            {
                goto done;
            }

            goto after;
        }

    done:
        result[t + 128] = Warp.ActiveMask();
        goto after;

#pragma warning disable CS0162 // Unreachable backward goto keeps the goto-loop range ending after the outer forward target
        if ((t & 1) == 0 && n++ < 1)
        {
            goto L1;
        }
#pragma warning restore CS0162

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void ForwardGotoAfterGateImplicitElseKernel(uint[] result)
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

    done:
        result[t + 128] = Warp.ActiveMask();
        goto after;

#pragma warning disable CS0162 // Unreachable backward goto keeps the goto-loop range ending after the forward target
        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }
#pragma warning restore CS0162

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void ForwardGotoAfterGateBeforeLastKernel(uint[] result)
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
            if ((t & 8) == 0)
            {
                goto done;
            }

            goto after;
        }
        else
        {
            goto after;
        }

    done:
        result[t + 128] = Warp.ActiveMask();
        goto after;

#pragma warning disable CS0162 // Unreachable backward goto keeps the goto-loop range ending after the forward target
        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }
#pragma warning restore CS0162

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void SwitchExitAfterGateBeforeLastKernel(uint[] result)
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

            switch (t & 8)
            {
                case 0:
                    goto after;
                default:
                    return;
            }
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void ForwardGotoDeadTailAfterGateBeforeLastKernel(uint[] result)
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
            goto after;
#pragma warning disable CS0162 // Unreachable statement tests jump-ending block detection before the loop end
            result[t + 128] = 1u;
#pragma warning restore CS0162
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void ForwardGotoToMemberLabelKernel(uint[] result)
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
            goto memberLabel;
        }

        result[t + 128] = Warp.ActiveMask();

    memberLabel:
        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void DoFalseAfterGateImplicitElseKernel(uint[] result)
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

            do
            {
                result[t + 96] = Warp.ActiveMask();
                goto after;
            }
            while (false);
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void WhileTrueAfterGateImplicitElseKernel(uint[] result)
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

            while (true)
            {
                result[t + 96] = Warp.ActiveMask();
                goto after;
            }
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void ForEverAfterGateImplicitElseKernel(uint[] result)
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

            for (;;)
            {
                result[t + 96] = Warp.ActiveMask();
                goto after;
            }
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void DeadLabelAfterGateImplicitElseKernel(uint[] result)
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
            goto after;
#pragma warning disable CS0162, CS0164 // Unreachable label tests branch-end reachability before loop exit
        deadLabel:
            result[t + 128] = 1u;
#pragma warning restore CS0162, CS0164
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void DeadLabelInNestedIfAfterGateImplicitElseKernel(uint[] result)
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
            goto after;
#pragma warning disable CS0162, CS0164 // Unreachable nested label tests branch-end reachability before loop exit
            if ((t & 8) == 0)
            {
            deadInner:
                result[t + 128] = 1u;
            }
#pragma warning restore CS0162, CS0164
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void DeadLabelInNestedBlockAfterGateImplicitElseKernel(uint[] result)
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
            goto after;
#pragma warning disable CS0162, CS0164 // Unreachable nested block label tests branch-end reachability before loop exit
            {
            deadInBlock:
                result[t + 128] = 1u;
            }
#pragma warning restore CS0162, CS0164
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }

    [GpuKernel]
    public static void SwitchDeadBreakAfterGateImplicitElseKernel(uint[] result)
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
            switch (t & 8)
            {
                case 0:
                    goto after;
#pragma warning disable CS0162 // Unreachable break tests branch-end reachability inside switch sections
                    break;
#pragma warning restore CS0162
                default:
                    return;
            }
        }

        if ((t & 16) == 0 && n++ < 1)
        {
            goto L1;
        }

    after:
        result[t + 160] = Warp.ActiveMask();
        result[t + 192] = (uint)n;
    }
}
