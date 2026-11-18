using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceGotoLoop
{
    [GpuKernel]
    public static void BackwardGotoLoopKernel(uint[] r)
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

        r[t] = m;
        r[t + 32] = Warp.ActiveMask();
    }
}
