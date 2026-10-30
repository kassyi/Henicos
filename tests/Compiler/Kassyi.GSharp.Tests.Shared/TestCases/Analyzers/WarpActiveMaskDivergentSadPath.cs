using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Warp.ActiveMask() under a condition that may differ between the threads (reported on the CPU dialects), and calls that
// every thread of the warp reaches together or under a uniform condition (not reported)
public class WarpActiveMaskDivergentSadPath
{
    [GpuKernel]
    public static void ActiveMaskKernel(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        r[t] = Warp.ActiveMask();
        if (t < 4)
        {
            r[t] = Warp.ActiveMask();
        }

        if (n > 0)
        {
            r[t] = Warp.ActiveMask();
        }

        r[t] = t % 2 == 0 ? Warp.ActiveMask() : 0u;
        for (int i = t; i < n; i += 32)
        {
            r[i] = Warp.ActiveMask();
        }

        bool any = t > 1 && Warp.ActiveMask() != 0u;
        r[t] += any ? 1u : 0u;
        Gpu.GridStrideLoop(n, i =>
        {
            r[i] = Warp.ActiveMask();
        });
    }

    [GpuDeviceFunction]
    public static uint MaskOfFirstLanes()
    {
        if (Gpu.LaneId() < 8)
        {
            return Warp.ActiveMask();
        }

        return 0u;
    }

    // Reported: the threads that returned early (the bounds guard), and in EarlyBreakKernel the ones that broke out of the
    // loop earlier in the same body, do not reach the call
    [GpuKernel]
    public static void EarlyExitKernel(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        r[t] = 0u;
        if (t >= 4)
        {
            return;
        }

        r[t] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void EarlyBreakKernel(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        for (int i = 0; i < n; i++)
        {
            if (t == i)
            {
                break;
            }

            r[t] = Warp.ActiveMask();
        }
    }

    // Not reported: every thread leaves the earlier loop and reaches the call; a return in a local function leaves only
    // the local function
    [GpuKernel]
    public static void ExitedLoopKernel(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        for (int i = 0; i < n; i++)
        {
            if (t == i)
            {
                break;
            }
        }

        void Clear()
        {
            if (t > 2)
            {
                return;
            }

            r[t] = 0u;
        }

        Clear();
        r[t] = Warp.ActiveMask();
    }

    // Reported: a device function does not know which arguments differ between the threads
    [GpuDeviceFunction]
    public static uint MaskIfLow(int lane)
    {
        if (lane < 4)
        {
            return Warp.ActiveMask();
        }

        return 0u;
    }

    // Not reported: an early return under a condition that is the same for every thread
    [GpuKernel]
    public static void UniformReturnKernel(uint[] r, int n)
    {
        int t = ThreadIndex.X;
        if (n <= 0)
        {
            return;
        }

        r[t] = Warp.ActiveMask();
    }
}
