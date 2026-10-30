using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A char switch flattened for its shuffles: goto case 66 names case 'B' by value (C# converts the int constant to the
// char, warning CS0469), so the flattened goto must reach the section of 'B', not fail to find one by spelling
public static class SwitchGotoCaseChar
{
    [GpuKernel]
    public static void SwitchGotoCaseCharKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        char letter = flag == 0 ? 'A' : 'C';
        switch (letter)
        {
            case 'A':
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                goto case 66;
            case 'B':
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 2) + 100;
                break;
            default:
                value += 1000;
                break;
        }

        output[tid] = value;
    }
}
