using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class KernelParameterRefStructKernels
{
    // GSH0044: ref parameter in GpuKernel
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithRefParam(ref int state)
    {
        state = 42;
    }

    // GSH0044: out parameter in GpuKernel
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithOutParam(out int result)
    {
        result = 100;
    }

    // GSH0044: ref struct (Span) parameter in GpuKernel
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithSpanParam(Span<int> buffer)
    {
        buffer[0] = 1;
    }
}
