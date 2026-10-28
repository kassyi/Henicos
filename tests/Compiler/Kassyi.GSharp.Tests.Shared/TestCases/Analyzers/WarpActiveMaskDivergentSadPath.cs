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
}
