using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Kernels whose value parameters are written before a barrier (#879); the CPU paths resume a kernel with its original
// arguments, as they resume a device function
public static class KernelParameterWrites
{
    [GpuKernel]
    public static void WrittenBeforeBarrierKernel(int value, int[] values, int[] replacement, int read, int[] output)
    {
        value = value + 1;
        values = replacement;
        Gpu.SyncThreads();
        output[ThreadIndex.X] = value + values[0] + read;
    }

    [GpuKernel]
    public static void WrittenWithoutBarrierKernel(int value, int[] output)
    {
        value = value + 1;
        output[ThreadIndex.X] = value;
    }
}
