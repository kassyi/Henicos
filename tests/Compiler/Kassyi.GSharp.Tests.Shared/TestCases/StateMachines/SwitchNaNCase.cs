using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A float switch flattened for its shuffle with a case float.NaN label: the dispatch tests NaN with temp != temp, since
// == never matches NaN while C# does
public static class SwitchNaNCase
{
    [GpuKernel]
    public static void SwitchNaNCaseKernel(int[] output, float selector)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (selector)
        {
            case float.NaN:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                break;
            case 1f:
                value += 100;
                break;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }
}
