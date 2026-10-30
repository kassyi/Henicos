using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A float switch flattened for its shuffle: goto case 1 names case 1f (C# converts the int constant to the float governing
// type, warning CS0469), so the flattened goto must compare the two as floating-point values of the governing type
public static class SwitchGotoCaseFloat
{
    [GpuKernel]
    public static void SwitchGotoCaseFloatKernel(int[] output, float selector)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (selector)
        {
            case 1f:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                break;
            case 2f:
                value += 100;
                goto case 1;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }
}
