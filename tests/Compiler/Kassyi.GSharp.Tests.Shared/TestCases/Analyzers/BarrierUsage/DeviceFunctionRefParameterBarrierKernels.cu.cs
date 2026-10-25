using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class DeviceFunctionRefParameterBarrierKernels
{
    // A device function that reaches a barrier may take ref and out parameters
    [GpuDeviceFunction]
    public static void IncrementAcrossBarrier(ref int value, out int previous)
    {
        previous = value;
        value += 1;
        Gpu.SyncThreads();
    }

    // A call that reaches only a warp shuffle may sit in a branch (see NonRecursiveDeviceFunctionBarrierKernels)
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuKernel]
    public static void RefParameterKernel(int[] buffer, int usePrimary)
    {
        int value = buffer[ThreadIndex.X];
        IncrementAcrossBarrier(ref value, out int previous);
        bool broadcast = usePrimary != 0 && Broadcast(value) > previous;
        buffer[ThreadIndex.X] = broadcast ? value : previous;
    }
}
