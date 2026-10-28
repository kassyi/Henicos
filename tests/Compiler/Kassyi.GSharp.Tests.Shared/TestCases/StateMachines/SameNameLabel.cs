using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Two labels named done end up in one state once the blocks are flattened. They start no state of their own (a goto
// reaches each from its own state); the managed output renames them, since C# cannot declare both in one block (CS0140)
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
