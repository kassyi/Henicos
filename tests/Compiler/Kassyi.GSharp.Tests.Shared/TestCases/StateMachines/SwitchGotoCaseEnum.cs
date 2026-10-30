using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public enum SwitchGotoCaseMode
{
    First,
    Second,
    Third
}

// An enum switch flattened for its shuffles: goto case (SwitchGotoCaseMode)2 names case Third by value, so the flattened
// goto must reach the section of Third
public static class SwitchGotoCaseEnum
{
    [GpuKernel]
    public static void SwitchGotoCaseEnumKernel(int[] output, int flag)
    {
        int tid = ThreadIndex.X;
        int value = tid;
        SwitchGotoCaseMode mode = flag == 0 ? SwitchGotoCaseMode.First : SwitchGotoCaseMode.Second;
        switch (mode)
        {
            case SwitchGotoCaseMode.First:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                goto case (SwitchGotoCaseMode)2;
            case SwitchGotoCaseMode.Second:
                value += 1000;
                break;
            case SwitchGotoCaseMode.Third:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 2) + 100;
                break;
        }

        output[tid] = value;
    }
}
