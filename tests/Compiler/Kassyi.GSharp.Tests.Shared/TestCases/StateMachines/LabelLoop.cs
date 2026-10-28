using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A label without a barrier anywhere: it must stay a label in the single state
public static class LabelLoop
{
    [GpuKernel]
    public static void LabelLoopKernel(int[] data, int rounds)
    {
        int tid = ThreadIndex.X;
        int acc = 0;
        int i = 0;
    loop:
        if (i < rounds)
        {
            acc += data[tid];
            i++;
            goto loop;
        }

        data[tid] = acc;
    }
}
