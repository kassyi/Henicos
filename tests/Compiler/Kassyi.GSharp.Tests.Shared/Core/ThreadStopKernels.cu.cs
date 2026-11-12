// ReSharper disable All
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A resource that writes <c>Id + 100</c> to <c>Log[Id]</c> when it is disposed.</summary>
public struct ThreadStopRecorder : IDisposable
{
    public int[] Log;
    public int Id;

    public void Dispose() => Log[Id] = Id + 100;
}

/// <summary>
/// A resource whose <c>Dispose</c> writes <c>Log[2] = 30</c>, then fails (a copy into a destination that is too short),
/// so <c>Log[3] = 40</c> is never written.
/// </summary>
public struct ThreadStopFailingRecorder : IDisposable
{
    public int[] Log;
    public int[] Source;

    public void Dispose()
    {
        Log[2] = 30;
        Source.AsSpan().CopyTo(Log.AsSpan(0, 1));
        Log[3] = 40;
    }
}

/// <summary>
/// Kernels in which a C# check fails in the middle of a thread: the thread stops at the failing check, so nothing it
/// would write after the check reaches the output, and the other threads of the launch run on, as on the GPU.
/// </summary>
public static partial class ThreadStopKernels
{
    // The first element of the first count elements of source; fails with InvalidOperationException when count is 0
    [GpuDeviceFunction]
    public static int FirstOf(int[] source, int count) => source.AsSpan(0, count).AsStackEnumerable().First();

    // Writes dst[0] = 1 and returns 1
    [GpuDeviceFunction]
    public static int Mark(int[] dst)
    {
        dst[0] = 1;
        return 1;
    }

    // Copies source into the first element of scratch (fails when source has more than one element), then writes
    // dst[2] = 3
    [GpuDeviceFunction]
    public static void CopyThenWrite(int[] source, int[] scratch, int[] dst)
    {
        source.AsSpan().CopyTo(scratch.AsSpan(0, 1));
        dst[2] = 3;
    }

    [GpuKernel]
    public static void SpanSliceKernel(int[] source, int[] dst, int start)
    {
        dst[0] = 1;
        Span<int> tail = source.AsSpan().Slice(start);
        dst[1] = 100 + tail.Length;
    }

    [GpuKernel]
    public static void SpanSliceCountKernel(int[] source, int[] dst, int start, int count)
    {
        dst[0] = 1;
        Span<int> part = source.AsSpan().Slice(start, count);
        dst[1] = 100 + part.Length;
    }

    [GpuKernel]
    public static void AsSpanStartKernel(int[] source, int[] dst, int start)
    {
        dst[0] = 1;
        Span<int> tail = source.AsSpan(start);
        dst[1] = 100 + tail.Length;
    }

    [GpuKernel]
    public static void CopyToShortKernel(int[] source, int[] scratch, int[] dst)
    {
        dst[0] = 1;
        source.AsSpan().CopyTo(scratch.AsSpan(0, 1));
        dst[1] = 2;
    }

    [GpuKernel]
    public static void FirstOfEmptyKernel(int[] source, int[] dst, int count)
    {
        dst[0] = 1;
        dst[1] = source.AsSpan(0, count).AsStackEnumerable().First();
    }

    // A pipeline that the compiler fuses into one loop
    [GpuKernel]
    public static void FusedFirstKernel(int[] source, int[] dst, int limit)
    {
        dst[0] = 1;
        dst[1] = source.AsStackEnumerable().Where(x => x > limit).First();
    }

    // The selector fails at the second element, inside the fused loop; the sum is written after the loop
    [GpuKernel]
    public static void FusedSelectorFailsKernel(int[] source, int[] dst)
    {
        dst[0] = 1;
        int sum = source.AsStackEnumerable().Select(x => x < 0 ? FirstOf(source, 0) : x).Sum();
        dst[1] = sum;
    }

    [GpuKernel]
    public static void DeviceFunctionKernel(int[] source, int[] scratch, int[] dst)
    {
        dst[0] = 1;
        CopyThenWrite(source, scratch, dst);
        dst[3] = 4;
    }

    // Mark runs before the right operand fails, so its write stays; the assignment does not happen
    [GpuKernel]
    public static void EvaluationOrderKernel(int[] source, int[] dst, int count)
    {
        dst[1] = Mark(dst) + source.AsSpan(0, count).AsStackEnumerable().First();
    }

    [GpuKernel]
    public static void AfterBarrierKernel(int[] source, int[] dst, int count)
    {
        dst[0] = 1;
        Gpu.SyncThreads();
        dst[1] = source.AsSpan(0, count).AsStackEnumerable().First();
        dst[2] = 3;
    }

    // The comparison fails; .NET reports InvalidOperationException for it
    [GpuKernel]
    public static void SortComparerFailsKernel(int[] data, int[] dst, int count)
    {
        dst[0] = 1;
        data.AsSpan().Sort((a, b) => (a < b ? -1 : (a > b ? 1 : 0)) + FirstOf(data, count));
        dst[1] = 2;
    }

    // The body fails; the resource is disposed as the failure leaves the using (C# runs Dispose in a finally)
    [GpuKernel]
    public static void UsingBodyFailsKernel(int[] source, int[] log, int count)
    {
        using (var resource = new ThreadStopRecorder { Log = log, Id = 1 })
        {
            log[0] = 1;
            log[2] = FirstOf(source, count);
            log[3] = 3;
        }

        log[4] = 4;
    }

    // The body fails, then Dispose fails too: as in C#, the failure of Dispose replaces the first one
    [GpuKernel]
    public static void UsingBodyAndDisposeFailKernel(int[] source, int[] log, int count)
    {
        using (var resource = new ThreadStopFailingRecorder { Log = log, Source = source })
        {
            log[0] = 1;
            log[1] = FirstOf(source, count);
        }

        log[4] = 4;
    }

    // Only Dispose fails, when the body ends normally
    [GpuKernel]
    public static void UsingDisposeFailsKernel(int[] source, int[] log)
    {
        using (var resource = new ThreadStopFailingRecorder { Log = log, Source = source })
        {
            log[0] = 1;
        }

        log[4] = 4;
    }

    // Thread 0 of the launch fails between its two writes; every other thread writes both
    [GpuKernel]
    public static void OtherThreadsRunOnKernel(int[] source, int[] dst, int count)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        dst[2 * t] = 1;
        int value = t == 0 ? FirstOf(source, count) : t + 10;
        dst[2 * t + 1] = value;
    }

    // Thread 0 fails before the block barrier; the other threads of its block pass the barrier without it
    [GpuKernel]
    public static void OtherThreadsPassBarrierKernel(int[] source, int[] dst, int count)
    {
        int t = ThreadIndex.X;
        dst[2 * t] = 1;
        if (t == 0)
        {
            dst[1] = FirstOf(source, count);
        }

        Gpu.SyncThreads();
        dst[2 * t + 1] = t + 10;
    }

    // Lane 0 fails before the ballot; the lanes that go on take part in it without lane 0
    [GpuKernel]
    public static void BallotAfterLaneFailsKernel(int[] source, int[] dst, int count)
    {
        int t = ThreadIndex.X;
        if (t == 0)
        {
            dst[0] = FirstOf(source, count);
        }

        uint lanes = Warp.BallotSync(0xFFFFFFFFu, true);
        dst[t] = (int)lanes;
    }

    // Thread 0 of block 0 fails before the grid sync: the other threads write before it, and the launch ends at the
    // grid sync, as the GPU lets its waiters leave the grid barrier once a thread has failed
    [GpuKernel(Cooperative = true)]
    public static void FailureBeforeGridSyncKernel(int[] source, int[] dst, int count)
    {
        int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int total = GridDimension.X * BlockDimension.X;
        dst[gid] = 1;
        if (gid == 0)
        {
            dst[gid] = FirstOf(source, count);
        }

        Gpu.SyncGrid();
        dst[total + gid] = 2;
    }
}
