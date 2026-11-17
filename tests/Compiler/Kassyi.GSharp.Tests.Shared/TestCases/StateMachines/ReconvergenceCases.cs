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
}
