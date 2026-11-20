using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class ReconvergenceSwitchJoinChain
{
    [GpuKernel]
    public static void GotoCaseChainKernel(uint[] r, int u)
    {
        int t = ThreadIndex.X;
        uint m0a = 0u;
        uint m0b = 0u;
        uint m1 = 0u;
        uint m2 = 0u;
        switch ((t % 3) + u)
        {
            case 0:
                m0a = Warp.ActiveMask();
                m0b = Warp.ActiveMask();
                goto case 1;
            case 1:
                m1 = Warp.ActiveMask();
                goto case 2;
            case 2:
                m2 = Warp.ActiveMask();
                break;
        }

        r[t] = m0a;
        r[t + 32] = m0b;
        r[t + 64] = m1;
        r[t + 96] = m2;
        r[t + 128] = Warp.ActiveMask();
    }
}
