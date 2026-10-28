using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// A barrier under a label must still split the state machine
public static class LabeledBarrier
{
#pragma warning disable CS0164 // The label under test has no goto
    [GpuKernel]
    public static void LabeledBarrierKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var buffer = new SharedMemory<int>(4);
        buffer[tid] = tid + 1;
    sync:
        Gpu.SyncThreads();
        output[tid] = buffer[(tid + 1) % 4];
    }
#pragma warning restore CS0164
}
