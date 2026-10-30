using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// An int switch flattened for its shuffles: goto case 1.0 and goto case 1.5 name case 1 (C# converts the double constant
// to the int governing type, warning CS0469), so the flattened gotos must reach the section of case 1
public static class SwitchGotoCaseIntFromDouble
{
    [GpuKernel]
    public static void SwitchGotoCaseIntFromDoubleKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        switch (flag)
        {
            case 0:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                goto case 1.0;
            case 1:
                value += 10;
                break;
            case 2:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 2);
                goto case 1.5;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }
}
