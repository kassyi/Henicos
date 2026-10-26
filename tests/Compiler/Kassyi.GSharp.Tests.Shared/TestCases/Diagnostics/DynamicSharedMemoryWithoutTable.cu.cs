using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: on CUDA a device function that reaches dynamic SharedMemory<T> takes the table of regions as a hidden argument,
// which a struct member and a delegate cannot pass on (#439)
public struct ScratchReader
{
    public int Count;

    public int Scratch => DynamicSharedMemoryWithoutTableKernels.Scratch(Count);
}

public static partial class DynamicSharedMemoryWithoutTableKernels
{
    [GpuDeviceFunction]
    public static int Scratch(int n)
    {
        var scratch = new SharedMemory<int>(n);
        scratch[ThreadIndex.X] = n;
        return scratch[ThreadIndex.X];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ComputedPropertyKernel(int[] output, int n)
    {
        var reader = new ScratchReader { Count = n };
        output[ThreadIndex.X] = reader.Scratch;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MethodGroupKernel(int[] output, int n)
    {
        Func<int, int> scratch = Scratch;
        output[ThreadIndex.X] = scratch(n);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DirectCallKernel(int[] output, int n)
    {
        output[ThreadIndex.X] = Scratch(n);
    }
}
