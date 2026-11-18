using Kassyi.GSharp.Core;

namespace IrReconvergence;

public static class IrReconvergenceCases
{
    [GpuDeviceFunction]
    public static void Step(uint[] r)
    {
        Gpu.SyncThreads();
        r[0] = 1u;
    }

    [GpuKernel]
    public static void ThreadVariantIf(uint[] r)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            r[t] = Warp.ActiveMask();
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void ThreadVariantIfElse(uint[] r)
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

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void UniformIf(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        if (n > 4)
        {
            r[t] = Warp.ActiveMask();
        }

        if (t < 4)
        {
            r[t] = 1u;
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void IfAsLastStatement(uint[] r)
    {
        int t = ThreadIndex.X;
        r[t] = Warp.ActiveMask();
        if (t < 4)
        {
            r[t + 32] = Warp.ActiveMask();
        }
    }

    [GpuDeviceFunction]
    public static void IfAsLastStatementDevice(uint[] r)
    {
        int t = ThreadIndex.X;
        r[t] = Warp.ActiveMask();
        if (t < 4)
        {
            r[t + 32] = Warp.ActiveMask();
        }
    }

    [GpuKernel]
    public static void ThreadVariantWhile(uint[] r)
    {
        int t = ThreadIndex.X;
        int k = 0;
        while (k < t % 4)
        {
            r[t * 4 + k] = Warp.ActiveMask();
            k++;
        }

        r[t] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void UniformWhile(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        int k = 0;
        while (k < n)
        {
            r[t] = Warp.ActiveMask();
            k++;
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void UniformWhileWithReturn(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        int k = 0;
        while (k < n)
        {
            if (k == t)
            {
                return;
            }

            r[t] = Warp.ActiveMask();
            k++;
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuDeviceFunction]
    public static void LeaveBothRegions(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            while (t < n)
            {
                if (r[t] == 0u)
                {
                    return;
                }

                r[t] = Warp.ActiveMask();
                t++;
            }
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void SuspendingCallRegion(uint[] r)
    {
        int t = ThreadIndex.X;
        Step(r);
        r[t] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void TwoIfs(uint[] r)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            r[t] = Warp.ActiveMask();
        }

        if (t >= 4)
        {
            r[t] = Warp.ActiveMask();
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void AndOperandRegion(uint[] r, bool u)
    {
        int t = ThreadIndex.X;
        bool ok = u && Warp.ActiveMask() > 4u;
        r[t] = ok ? 1u : 0u;
    }

    [GpuKernel]
    public static void TernaryUniform(uint[] r, bool u)
    {
        int t = ThreadIndex.X;
        uint v = u ? Warp.ActiveMask() : 0u;
        r[t] = v;
    }

    [GpuKernel]
    public static void TernaryVariant(uint[] r)
    {
        int t = ThreadIndex.X;
        uint v = t < 4 ? Warp.ActiveMask() : 0u;
        r[t] = v;
    }

    [GpuKernel]
    public static void EmbeddedRegion(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        if (n > 0)
            if (t < 4)
                r[t] = Warp.ActiveMask();

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CommentedIf(uint[] r)
    {
        int t = ThreadIndex.X;
        // note
        if (t < 4)
        {
            r[t] = Warp.ActiveMask();
        }

        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CommentedWhile(uint[] r)
    {
        int t = ThreadIndex.X;
        int k = 0;
        // loop
        while (Warp.ActiveMask() > (uint)(t + k))
        {
            k++;
        }

        r[t] = (uint)k;
    }

    [GpuKernel]
    public static void ArriveSpan(int[] data, int[] masks)
    {
        int tid = ThreadIndex.X;
        System.Span<int> s = data;
        if (tid % 2 == 0)
        {
            Gpu.SyncWarp(0x55555555u);
            return;
        }

        s[tid] = 1;
        masks[tid] = (int)Warp.ActiveMask();
    }

    [GpuKernel]
    public static void NotReachingActiveMask(int[] a)
    {
        int t = ThreadIndex.X;
        if (t < 4)
        {
            Gpu.SyncWarp(0x1u);
        }

        a[t] = 1;
    }
}
