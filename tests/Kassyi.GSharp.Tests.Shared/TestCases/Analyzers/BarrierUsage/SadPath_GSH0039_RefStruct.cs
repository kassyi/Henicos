using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SadPath_GSH0039_RefStruct
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public unsafe static void CrossBarrierSpanKernel(int* buffer)
    {
        Span<int> localSpan = new Span<int>(buffer, 10);
        localSpan[0] = 1;

        // GSH0039: Span crosses barrier and is used afterwards
        Gpu.SyncThreads();

        localSpan[1] = 2;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void CrossBarrierRefLocalKernel(int[] buffer)
    {
        ref int first = ref buffer[0];
        first = 10;

        // GSH0039: ref local crosses barrier and is used afterwards
        Gpu.SyncThreads();

        first = 20;
    }
}
