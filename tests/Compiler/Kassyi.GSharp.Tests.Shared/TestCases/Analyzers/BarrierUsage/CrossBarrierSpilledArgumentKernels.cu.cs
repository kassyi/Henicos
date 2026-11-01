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

    // GSH0039: the srcLane argument of a warp shuffle is spilled before the shuffle splits, so Advance(ref s) changes
    // the parameter before the split and s[0] reads the reset parameter after it (#1004 F3)
    [GpuDeviceFunction]
    public static int AdvancedInShuffleLaneArgument(Span<int> s)
    {
        int v = Warp.ShuffleSync(0xFFFFFFFFu, 0, Advance(ref s));
        return s[0] + v;
    }

    // GSH0039: s[0] in the srcLane argument of the shuffle is evaluated before the shuffle, which is after the earlier
    // barrier (#1004 F3)
    [GpuDeviceFunction]
    public static int SpanReadInShuffleLaneAfterBarrier(Span<int> s, int v)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        return Warp.ShuffleSync(0xFFFFFFFFu, v, s[0]);
    }

    // No diagnostic: both s[0] (var) and s[1] (srcLane) are spilled into temporaries before the shuffle splits (#1004 F3)
    [GpuDeviceFunction]
    public static int ShuffleValueAndLaneFromSlicedSpan(Span<int> s)
    {
        s = s.Slice(1);
        return Warp.ShuffleSync(0xFFFFFFFFu, s[0], s[1]);
    }

    // No diagnostic: a local span read in both var and srcLane of a single shuffle is evaluated before the split (#1004 F3)
    [GpuKernel]
    public static void LocalSpanInShuffleValueAndLaneKernel(int[] buffer, int[] output)
    {
        Span<int> s = buffer;
        output[0] = Warp.ShuffleSync(0xFFFFFFFFu, s[0], s[1]);
    }
}
