using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Analyzer-only case")]
public static partial class CrossBarrierSpilledArgumentKernels
{
    [GpuDeviceFunction]
    public static int Advance(ref Span<int> s)
    {
        s = s.Slice(1);
        return 0;
    }

    [GpuDeviceFunction]
    public static int HeadAfterBarrier(int offset, Span<int> x)
    {
        Gpu.SyncThreads();
        return x[offset];
    }

    // GSH0039: an earlier argument changes the span, and the call reads it again when it resumes. A span cannot be
    // kept in a temporary across the split, so the lowering leaves it in place, and the liveness follows the same rule
    // (SpilledArguments)
    [GpuDeviceFunction]
    public static int AdvancedByEarlierArgument(Span<int> s) => HeadAfterBarrier(Advance(ref s), s);
}
