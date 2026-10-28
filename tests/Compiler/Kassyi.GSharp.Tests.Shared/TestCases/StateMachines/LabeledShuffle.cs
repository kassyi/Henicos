using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A shuffle under a label: the temporary that holds its result must come after the label, so that every goto runs the
// shuffle again
public static class LabeledShuffle
{
    [GpuKernel]
    public static void LabeledShuffleKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int v = data[tid];
        int acc = 0;
        int k = 0;
    again:
        acc += Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1);
        v += 1;
        if (++k < 3)
        {
            goto again;
        }

        data[tid] = acc;
    }
}
