using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Kernels that reach Warp.ActiveMask(). Mark(n) is the first call where the lanes of a thread-dependent branch or loop
// meet again: the statement after it, or the incrementor of a loop that some lanes continue
public static class ReconvergenceCases
{
    [GpuDeviceFunction]
    public static void Mark(int id)
    {
    }

    [GpuDeviceFunction]
    public static int Next(int k) => k + 1;

    [GpuKernel]
    public static void IfKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            r[t] = Warp.ActiveMask();
        }

        Mark(1);
        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void IfElseKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        if ((t & 1) == 0)
        {
            r[t] = Warp.ActiveMask();
        }
        else
        {
            r[t] = Warp.ActiveMask() + 1u;
        }

        Mark(2);
        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void DivergentForKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        for (int k = 0; k < t % 4; k++)
        {
            r[t * 4 + k] = Warp.ActiveMask();
        }

        Mark(3);
        r[t] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void BreakKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int k = 0;
        while (true)
        {
            if (k >= t % 4)
            {
                break;
            }

            r[t * 4 + k] = Warp.ActiveMask();
            k++;
        }

        Mark(4);
        r[t] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ContinueKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        for (int k = 0; k < 3; k = Next(k))
        {
            if (((t >> k) & 1) == 0)
            {
                r[t * 6 + k * 2] = Warp.ActiveMask();
                continue;
            }

            r[t * 6 + k * 2 + 1] = Warp.ActiveMask();
        }
    }

    [GpuKernel]
    public static void NestedKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        for (int k = 0; k < t % 3; k++)
        {
            if ((t & 1) == 0)
            {
                r[t * 4 + k] = Warp.ActiveMask();
            }

            Mark(5);
        }

        Mark(6);
        r[t] = Warp.ActiveMask();
    }

    // Not divergent, or nothing to wait for inside: no reconvergence point
    [GpuKernel]
    public static void UniformAndPlainKernel(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        if (n > 4)
        {
            r[t] = Warp.ActiveMask();
        }

        Mark(7);
        if (t < 4)
        {
            r[t] = 1u;
        }

        Mark(8);
        r[t + 32] = Warp.ActiveMask();
    }

    // The branch ends the kernel: the lanes exit there
    [GpuKernel]
    public static void LastStatementKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            r[t] = Warp.ActiveMask();
        }
    }

    [GpuKernel]
    public static void UniformSwitchBreakKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        switch (u)
        {
            case 0:
                r[t] = Warp.ActiveMask();
                if ((t & 1) == 0)
                {
                    break;
                }

                r[t + 32] = Warp.ActiveMask();
                break;
        }

        Mark(9);
        r[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void UniformSwitchDirectBreakKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        switch (u)
        {
            case 0:
                r[t] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        Mark(10);
    }

    [GpuKernel]
    public static void ForwardGotoKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        r[t] = Warp.ActiveMask();
        if ((t & 1) != 0)
        {
            goto L;
        }

        r[t + 32] = Warp.ActiveMask();
    L:
        Mark(11);
        r[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoOutOfUniformIfKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        r[t] = Warp.ActiveMask();
        if (u == 0)
        {
            if ((t & 1) != 0)
            {
                goto L;
            }

            r[t + 32] = Warp.ActiveMask();
        }

    L:
        Mark(12);
        r[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CrossingGotosKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        if ((t & 1) != 0)
        {
            goto fail;
        }

        r[t] = Warp.ActiveMask();
        if ((t & 2) != 0)
        {
            goto done;
        }

    fail:
        Mark(13);
        r[t + 32] = Warp.ActiveMask();
    done:
        Mark(14);
        r[t + 64] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ForwardGotoNoSplitKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        if ((t & 1) != 0)
        {
            goto L;
        }

        r[t] = 1u;
    L:
        Mark(15);
    }

    [GpuKernel]
    public static void ForwardGotoOverNestedBlockKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        uint m = 0u;
        if ((t & 1) != 0)
        {
            goto L;
        }

        {
            r[t] = 1u;
            r[t] = 2u;
            r[t] = 3u;
            r[t] = 4u;
            m = Warp.ActiveMask();
        }

    L:
        Mark(16);
        r[t + 32] = m;
    }

    [GpuKernel]
    public static void BackwardGotoKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        Mark(17);
        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void BackwardGotoNoSplitKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = 1u;
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            goto top;
        }

        Mark(18);
    }

    [GpuKernel]
    public static void GotoLoopAtKernelEndKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t + (n * 32)] = Warp.ActiveMask();
        n++;
        if ((t & 1) == 1 && n < 2)
        {
            Mark(19);
            r[t + 64] = Warp.ActiveMask();
            goto top;
        }
    }

    [GpuKernel]
    public static void GotoCaseJoinKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        switch (t & 1)
        {
            case 0:
                r[t] = Warp.ActiveMask();
                r[t + 32] = Warp.ActiveMask();
                goto case 1;
            case 1:
                Mark(20);
                r[t + 64] = Warp.ActiveMask();
                break;
        }

        Mark(21);
        r[t + 96] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLabelJoinKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        switch (t & 1)
        {
            case 0:
                r[t] = Warp.ActiveMask();
                r[t + 32] = Warp.ActiveMask();
                goto shared;
            case 1:
                r[t + 64] = Warp.ActiveMask();
            shared:
                Mark(22);
                r[t + 96] = Warp.ActiveMask();
                break;
        }

        Mark(23);
        r[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ThreadVariantSwitchNoCrossJumpKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        switch (t & 1)
        {
            case 0:
                r[t] = Warp.ActiveMask();
                break;
            case 1:
                r[t + 32] = Warp.ActiveMask();
                break;
        }

        Mark(24);
    }

    [GpuKernel]
    public static void UniformSwitchGotoCaseNoJoinKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        switch (u)
        {
            case 0:
                r[t] = Warp.ActiveMask();
                if ((t & 1) != 0)
                {
                    goto case 1;
                }

                break;
            case 1:
                r[t + 32] = Warp.ActiveMask();
                break;
        }

        Mark(25);
    }

    [GpuKernel]
    public static void WhenSwitchNoJoinKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        switch (t & 1)
        {
            case 0 when u == 0:
                r[t] = Warp.ActiveMask();
                goto case 1;
            case 1:
                r[t + 32] = Warp.ActiveMask();
                break;
        }

        Mark(26);
    }

    [GpuKernel]
    public static void ForwardGotoToSwitchJoinLabelKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        switch ((t % 3) + u)
        {
            case 0:
                r[t] = Warp.ActiveMask();
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

                r[t + 32] = Warp.ActiveMask();
            shared:
                r[t + 64] = Warp.ActiveMask();
                break;
            default:
                goto shared;
        }

        Mark(27);
    }

    // Warp.ActiveMask() in the right operand of &&: only the odd lanes evaluate it, a thread-dependent branch once the
    // operand is a statement, so the lanes meet again before Mark(20)
    [GpuKernel]
    public static void ActiveMaskInAndAlsoKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        bool odd = (t & 1) == 1 && Warp.ActiveMask() != 0u;
        Mark(20);
        r[t] = odd ? 1u : 0u;
        r[t + 32] = Warp.ActiveMask();
    }

    // A goto loop whose last statement is a block ends right after the if that holds its last goto back to top: the
    // lanes meet again before Mark(28), inside the block
    [GpuKernel]
    public static void GotoLoopInBlockKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        {
            if ((t & 1) == 1 && n++ < 1)
            {
                goto top;
            }

            Mark(28);
            r[t + 32] = Warp.ActiveMask();
        }

        Mark(29);
    }

    // The block of a goto loop holds a second goto loop (again) around the statement with the last goto back to top:
    // again closes inside its own block, right after its goto (before Mark(30)), and top closes inside the block too,
    // right after the if that holds its goto (before r[t + 32])
    [GpuKernel]
    public static void GotoLoopInBlockAroundInnerLoopKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    top:
        r[t] = Warp.ActiveMask();
        {
        again:
            {
                if ((t & 2) != 0 && m++ < 1)
                {
                    goto again;
                }

                Mark(30);
                if ((t & 1) == 1 && n++ < 1)
                {
                    goto top;
                }

                r[t + 32] = Warp.ActiveMask();
            }
        }

        Mark(31);
    }

    // A goto loop whose last statement is a thread-dependent if: the loop ends inside the if, right after the if that
    // holds its goto; the if ends before Mark(32)
    [GpuKernel]
    public static void GotoLoopInIfKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        if ((t & 2) != 0)
        {
            if ((t & 1) == 1 && n++ < 1)
            {
                goto top;
            }

            r[t + 32] = Warp.ActiveMask();
        }

        Mark(32);
    }

    // A goto loop whose last statement is an if with a pattern default switch after the gate: Mark(33) must be the
    // gate of top even though the switch uses case var unused: return; instead of default:
    [GpuKernel]
    public static void GotoLoopSwitchVarPatternBeforeLastKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto top;
            }

            Mark(33);
            r[t + 32] = Warp.ActiveMask();
            switch (t & 8)
            {
                case 0:
                    goto after;
                case var unused:
                    return;
            }
        }

        Mark(34);
        if ((t & 16) == 0 && n++ < 1)
        {
            goto top;
        }

    after:
        Mark(35);
        r[t + 64] = Warp.ActiveMask();
    }

    // A goto loop whose last statement is an if with a checked block holding an unreachable label after a jump:
    // ReachesEnd matches StatementFlow, so the loop closes at the gate before Mark(36) without reconciling else
    [GpuKernel]
    public static void GotoLoopCheckedDeadLabelBeforeLastKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        if ((t & 4) == 0)
        {
            if ((t & 3) != 0 && n++ < 2)
            {
                goto top;
            }

            checked
            {
                Mark(36);
                r[t + 32] = Warp.ActiveMask();
                goto after;
#pragma warning disable CS0162, CS0164 // Unreachable label tests ReachesEnd consistency inside checked blocks
            deadChecked:
                r[t + 64] = 1u;
#pragma warning restore CS0162, CS0164
            }
        }

        Mark(37);
        if ((t & 16) == 0 && n++ < 1)
        {
            goto top;
        }

    after:
        Mark(38);
        r[t + 96] = Warp.ActiveMask();
    }

    // A goto loop wrapping nested for loops where the inner for has a forward-goto gate past the outer loop and the
    // outer loop uses continue: the inner for's normal exit remains in the goto loop, so the outer loop keeps its
    // continue body region and closes the goto loop only on exit.
    [GpuKernel]
    public static void GotoLoopInNestedForWithOuterContinueKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    top:
        r[t] = Warp.ActiveMask();
        for (int i = 0; i < u + 2; i++)
        {
            if (i == 0 && (t & 8) == 0)
            {
                continue;
            }

            for (int j = 0; j < u + 2; j++)
            {
                if ((t & 4) == 0)
                {
                    if ((t & 3) != 0 && n++ < 2)
                    {
                        goto top;
                    }

                    Mark(39);
                    r[t + 32] = Warp.ActiveMask();
                    goto after;
                }

                r[t + 64] = Warp.ActiveMask();
            }

            Mark(40);
        }

    after:
        Mark(41);
        r[t + 96] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopWithContinueBeforeGateInForKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        r[t + 32 * n] = Warp.ActiveMask();
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

                Mark(42);
                r[t + 96] = Warp.ActiveMask();
                break;
            }

            r[t + 128] = Warp.ActiveMask();
        }

        Mark(43);
        r[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopWithLabeledBreakIntoLoopInNestedForKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        r[t + 32 * n] = Warp.ActiveMask();
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

                    Mark(44);
                    r[t + 96] = Warp.ActiveMask();
                    return;
                }
            }

            Mark(45);
            r[t + 128] = Warp.ActiveMask();
            return;
        }

        if ((t & 8) == 0 && n++ < 1)
        {
            goto L1;
        }

        Mark(46);
        r[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void NestedGotoLoopsWithContinueAndBreakExitingInnerLoopKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L0:
        r[t + 32 * n] = Warp.ActiveMask();
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

            Mark(47);
            r[t + 96] = Warp.ActiveMask();
            return;
        }

        Mark(48);
        r[t + 128] = Warp.ActiveMask();
        if ((t & 16) == 0 && n++ < 1)
        {
            goto L0;
        }

        Mark(49);
        r[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopWithConditionalForwardGotoToMemberBeforeGateKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        r[t + 32 * n] = Warp.ActiveMask();
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

            Mark(50);
            r[t + 96] = Warp.ActiveMask();
            return;
        }

    M:
        if ((t & 8) == 0 && n++ < 1)
        {
            goto L1;
        }

        Mark(51);
        r[t + 128] = Warp.ActiveMask();
    }

    [GpuDeviceFunction]
    public static void GotoInForWithEarlyReturn(uint[] r, int t, int u)
    {
        int n = 0;
    L1:
        r[t + 32 * n] = Warp.ActiveMask();
        for (int j = 0; j < u + 2; j++)
        {
            if ((t & 4) == 0)
            {
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                Mark(52);
                r[t + 96] = Warp.ActiveMask();
                return;
            }
        }

        Mark(53);
        r[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopTopLevelForwardGotoToMemberPastReturnGateKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            r[t + 32 * n] = Warp.ActiveMask();
        }

        if ((t & 3) != 0 && n++ < 2)
        {
            goto L1;
        }

        if ((t & 8) == 0)
        {
            goto M;
        }

        Mark(54);
        r[t + 96] = Warp.ActiveMask();
        return;
    M:
        if ((t & 4) == 0 && m++ < 1)
        {
            goto L1;
        }

        Mark(55);
        r[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInForForwardGotoToMemberPastBreakGateKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            r[t + 32 * n] = Warp.ActiveMask();
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

                Mark(56);
                r[t + 96] = Warp.ActiveMask();
                break;
            M:
                if ((t & 1) == 0 && m++ < 1)
                {
                    goto L1;
                }

                Mark(57);
                r[t + 128] = Warp.ActiveMask();
                break;
            }
        }

        Mark(58);
        r[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopPlainBlockForwardGotoToMemberPastReturnGateKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            r[t + 32 * n] = Warp.ActiveMask();
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

            Mark(59);
            r[t + 96] = Warp.ActiveMask();
            return;
        }

    M:
        if ((t & 4) == 0 && m++ < 1)
        {
            goto L1;
        }

        Mark(60);
        r[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInIfForwardGotoToMemberPastReturnGateKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            r[t + 32 * n] = Warp.ActiveMask();
        }

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

            Mark(61);
            r[t + 96] = Warp.ActiveMask();
            return;
        M:
            if ((t & 1) == 0 && m++ < 1)
            {
                goto L1;
            }

            Mark(62);
            r[t + 128] = Warp.ActiveMask();
            return;
        }

        Mark(63);
        r[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CheckedBlockForwardGotoToMemberPastReturnGateKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            r[t + 32 * n] = Warp.ActiveMask();
        }

        if ((t & 3) != 0 && n++ < 2)
        {
            goto L1;
        }

        checked
        {
            if ((t & 8) == 0)
            {
                goto M;
            }

            Mark(64);
            r[t + 96] = Warp.ActiveMask();
            return;
        }

    M:
        if ((t & 4) == 0 && m++ < 1)
        {
            goto L1;
        }

        Mark(65);
        r[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GateWithForwardGotoToMemberPastReturnKernel(uint[] r)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
    L1:
        if (m == 0)
        {
            r[t + 32 * n] = Warp.ActiveMask();
        }

        if ((t & 3) != 0 && n++ < 2)
        {
            goto L1;
        }

        if ((t & 8) == 0)
        {
            goto M;
        }

        if ((t & 4) == 0)
        {
            goto K;
        }

        Mark(90);
        r[t + 96] = Warp.ActiveMask();
        return;
    M:
        if ((t & 2) == 0 && m++ < 1)
        {
            goto L1;
        }

        Mark(91);
        r[t + 128] = Warp.ActiveMask();
        return;
    K:
        r[t + 160] = Warp.ActiveMask();
        Mark(92);
    }

    [GpuKernel]
    public static void LoopWithContinueAndUnreachableEndKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int j = 0; j < u + 2; j++)
        {
            if (j == 0)
            {
                continue;
            }

            if ((t & 4) == 0)
            {
                break;
            }

            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            Mark(66);
            result[t + 96] = Warp.ActiveMask();
            return;
        }

        Mark(67);
        result[t + 128] = Warp.ActiveMask();
        if ((t & 8) == 0 && n++ < 1)
        {
            goto L1;
        }

        Mark(68);
        result[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void LoopWithoutContinueAndUnreachableEndKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        for (int j = 0; j < u + 2; j++)
        {
            if ((t & 4) == 0)
            {
                break;
            }

            if ((t & 3) != 0 && n++ < 2)
            {
                goto L1;
            }

            Mark(69);
            result[t + 96] = Warp.ActiveMask();
            return;
        }

        Mark(70);
        result[t + 128] = Warp.ActiveMask();
        if ((t & 8) == 0 && n++ < 1)
        {
            goto L1;
        }

        Mark(71);
        result[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void DoWhileFalseWithInnerForEarlyReturnKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        do
        {
            for (int j = 0; j < u + 2; j++)
            {
                if ((t & 4) == 0)
                {
                    if ((t & 3) != 0 && n++ < 2)
                    {
                        goto L1;
                    }

                    Mark(72);
                    result[t + 96] = Warp.ActiveMask();
                    return;
                }
            }

            if ((t & 8) == 0 && n++ < 1)
            {
                goto L1;
            }
        }
        while (false);

        Mark(73);
        result[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInSwitchWithoutDefaultKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch ((t & 4) + u)
        {
            case 0:
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                Mark(74);
                result[t + 96] = Warp.ActiveMask();
                break;
        }

        Mark(75);
        result[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInSwitchWithMultipleSectionsAndBreakKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch ((t & 4) + (t & 8) + u)
        {
            case 0:
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                Mark(76);
                result[t + 96] = Warp.ActiveMask();
                break;
            case 4:
                Mark(77);
                result[t + 128] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        Mark(78);
        result[t + 160] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInSwitchSectionWithIfReconcileKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch ((t & 4) + u)
        {
            case 0:
                if ((t & 8) == 0)
                {
                    if ((t & 3) != 0 && n++ < 2)
                    {
                        goto L1;
                    }

                    Mark(79);
                    result[t + 96] = Warp.ActiveMask();
                }

                Mark(80);
                break;
            default:
                break;
        }

        Mark(81);
        result[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInUniformSwitchSectionKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch (u)
        {
            case 0:
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                Mark(82);
                result[t + 96] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        Mark(83);
        result[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInWhenPatternSwitchKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch ((t & 4) + u)
        {
            case 0 when u == 0:
                if ((t & 3) != 0 && n++ < 2)
                {
                    goto L1;
                }

                Mark(84);
                result[t + 96] = Warp.ActiveMask();
                break;
        }

        Mark(85);
        result[t + 128] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void GotoLoopInSwitchSectionWithSelfGotoCaseKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int k = 0;
    L1:
        result[t + 32 * n] = Warp.ActiveMask();
        switch ((t & 4) + u)
        {
            case 0:
                result[t + 32 * (2 + k)] = Warp.ActiveMask();
                if ((t & 3) == 2 && k < 1)
                {
                    k++;
                    goto case 0;
                }

                if ((t & 3) != 0 && n < 1)
                {
                    n++;
                    goto L1;
                }

                Mark(86);
                result[t + 128] = Warp.ActiveMask();
                break;
            default:
                break;
        }

        Mark(87);
        result[t + 160] = Warp.ActiveMask();
    }
}
