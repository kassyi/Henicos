using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Two labels named done end up in one state once the blocks are flattened; the managed output cannot declare both in one
// block (CS0140), so labels whose name appears twice always start a state
public static class SameNameLabel
{
    [GpuKernel]
    public static void SameNameLabelKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int acc = t;
        {
            int step = 10;
            Gpu.SyncThreads();
            if ((t & 1) == 0)
            {
                acc += step;
                goto done;
            }

            acc += 100;
            done: ;
        }

        {
            int step = 1000;
            if ((t & 1) != 0)
            {
                acc += step;
                goto done;
            }

            acc += 2000;
            done: ;
            Gpu.SyncThreads();
        }

        output[t] = acc;
    }
}
