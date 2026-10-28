using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// done starts a state (the goto after the barrier reaches it). extra is declared before done and used after it, so it
// must live in the context; before is used only before done and stays a local. Both are declared without an
// initializer, as a goto past an initialized declaration is ill-formed C++.
public static class CrossLabelWrite
{
    [GpuKernel]
    public static void CrossLabelWriteKernel(int[] data, int flag)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        if (flag != 0)
        {
            Gpu.SyncThreads();
            value += 1;
            goto done;
        }

        int before;
        before = 3;
        value += before;
        int extra;
        extra = 7;
        value += extra;
    done:
        extra = value * 2;
        data[tid] = extra;
    }
}
