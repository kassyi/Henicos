using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceSwitchJoin
{
    [GpuKernel]
    public static void GotoCaseJoinKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        uint m1 = 0u;
        uint m2 = 0u;
        uint m3 = 0u;
        switch ((t & 1) + u)
        {
            case 0:
                m1 = Warp.ActiveMask();
                m2 = Warp.ActiveMask();
                goto case 1;
            case 1:
                m3 = Warp.ActiveMask();
                break;
        }

        r[t] = m1;
        r[t + 32] = m2;
        r[t + 64] = m3;
        r[t + 96] = Warp.ActiveMask();
    }
}
