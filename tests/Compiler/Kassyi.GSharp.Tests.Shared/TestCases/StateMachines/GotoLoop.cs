using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A goto loop that holds a split point: the lanes enter the region before the label and reconverge after the last
// backward goto
public static class GotoLoop
{
    [GpuKernel]
    public static void GotoLoopKernel(uint[] result)
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

        result[t] = m;
        result[t + 32] = Warp.ActiveMask();
    }
}
