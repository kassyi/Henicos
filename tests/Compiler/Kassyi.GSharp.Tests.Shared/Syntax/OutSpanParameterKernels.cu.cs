// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A span parameter of a device function that changes before a split and is overwritten through an <c>out</c> argument
/// after it, before any read: the value from before the split is never read again, so the state machine does not keep
/// it (no GSH0039, #1003).
/// </summary>
public static partial class OutSpanParameterKernels
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

    // Changed, a barrier, then overwritten through out before the read
    [GpuDeviceFunction]
    public static int ReassignedAfterBarrier(int[] buffer, Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        Fill(buffer, out s);
        return s[0];
    }

    // Changed, then a shuffle and an out argument in the same statement: the shuffle splits first
    [GpuDeviceFunction]
    public static int ReassignedAfterShuffle(int[] buffer, Span<int> s)
    {
        s = s.Slice(1);
        int a = Warp.ShuffleSync(FullMask, 0, 0) + FillAndReturn(buffer, out s);
        return a + s[0];
    }

    [GpuKernel]
    public static void OutSpanKernel(int[] input, int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = ReassignedAfterBarrier(buffer, input) * 1000 + ReassignedAfterShuffle(buffer, input);
    }
}
