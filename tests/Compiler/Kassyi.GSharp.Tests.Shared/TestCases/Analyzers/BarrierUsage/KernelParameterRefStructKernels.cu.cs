using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Analyzer-only case")]
public static partial class KernelParameterRefStructKernels
{
    // ref parameter in GpuKernel is permitted across barriers
    [GpuKernel]
    public static void KernelWithRefParam(ref int state)
    {
        state = 42;
        Gpu.SyncThreads();
        state += 1;
    }

    // out parameter in GpuKernel is permitted across barriers
    [GpuKernel]
    public static void KernelWithOutParam(out int result)
    {
        result = 100;
        Gpu.SyncThreads();
    }

    // in parameter in GpuKernel is permitted across barriers
    [GpuKernel]
    public static void KernelWithInParam(in int input, int[] output)
    {
        int value = input;
        Gpu.SyncThreads();
        output[0] = value;
    }

    // ref readonly parameter in GpuKernel is permitted across barriers
    [GpuKernel]
    public static void KernelWithRefReadOnlyParam(ref readonly int input, int[] output)
    {
        int value = input;
        Gpu.SyncThreads();
        output[0] = value;
    }

    // ref struct (Span) parameter in GpuKernel is permitted across barriers
    [GpuKernel]
    public static void KernelWithSpanParam(Span<int> buffer)
    {
        buffer[0] = 1;
        Gpu.SyncThreads();
        buffer[0] += 1;
    }
}
