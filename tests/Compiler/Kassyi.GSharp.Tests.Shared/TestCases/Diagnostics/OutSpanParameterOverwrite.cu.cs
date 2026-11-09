using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class OutSpanOverwriteKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    [GpuDeviceFunction]
    public static void Fill(int[] buffer, out Span<int> s)
    {
        s = buffer;
    }

    [GpuDeviceFunction]
    public static int FillAndReturn(int[] buffer, out Span<int> s)
    {
        s = buffer;
        return 1;
    }

    // Overwritten through out after the barrier, before any read: not live across it
    [GpuDeviceFunction]
    public static int ReassignedAfterBarrier(int[] buffer, Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        Fill(buffer, out s);
        return s[0];
    }

    // Overwritten through out after a shuffle of the same statement
    [GpuDeviceFunction]
    public static int ReassignedAfterShuffle(int[] buffer, Span<int> s)
    {
        s = s.Slice(1);
        int a = Warp.ShuffleSync(FullMask, 0, 0) + FillAndReturn(buffer, out s);
        return a + s[0];
    }

    // Read after the barrier before it is overwritten: live across it (GSH0039)
    [GpuDeviceFunction]
    public static int ReadBeforeOverwrite(int[] buffer, Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        int first = s[0];
        Fill(buffer, out s);
        return first + s[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void OutSpanOverwriteKernel(int[] input, int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = ReassignedAfterBarrier(buffer, input) + ReassignedAfterShuffle(buffer, input) +
                      ReadBeforeOverwrite(buffer, input);
    }
}
