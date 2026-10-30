using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A float switch flattened for its shuffle: goto case 0.1 names case 0.1f, because C# rounds the double constant 0.1 to
// the float governing type first; compared as doubles, 0.1 and 0.1f differ
public static class SwitchGotoCaseFloatRounded
{
    [GpuKernel]
    public static void SwitchGotoCaseFloatRoundedKernel(int[] output, float selector)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (selector)
        {
            case 0.1f:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                break;
            case 0.2f:
                value += 100;
                goto case 0.1;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }
}
