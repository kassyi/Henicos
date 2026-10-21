using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class NonRecursiveDeviceFunctionBarrierKernels
{
    [GpuDeviceFunction]
    public static int Exchange(int value, int length)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> shared = new(256);
        shared[tid] = value;
        Gpu.SyncThreads();
        return shared[length - 1 - tid];
    }

    // A barrier-reaching call nested two levels deep, called repeatedly without recursion
    [GpuDeviceFunction]
    public static int ExchangeTwice(int value, int length)
    {
        int once = Exchange(value, length);
        return Exchange(once, length);
    }

    [GpuDeviceFunction]
    public static void ReverseWarp(Span<int> data)
    {
        int lane = ThreadIndex.X & 31;
        data[lane] = Warp.ShuffleSync(0xFFFFFFFFu, data[lane], 31 - lane);
    }

    [GpuKernel]
    public static void DeviceFunctionBarrierKernel(int[] input, int[] output, int length, int usePrimary)
    {
        int tid = ThreadIndex.X;
        output[tid] = ExchangeTwice(input[tid], length);

        if (usePrimary != 0)
        {
            ReverseWarp(output);
        }
    }
}
