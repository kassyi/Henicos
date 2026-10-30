using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

/// <summary>A ref struct whose field a device function changes in its by-value parameter.</summary>
public ref struct ParameterCursor
{
    public int Position;
}

/// <summary>Two spans that a deconstruction assigns to two variables.</summary>
public ref struct SpanPair
{
    public Span<int> First;
    public Span<int> Second;

    public readonly void Deconstruct(out Span<int> first, out Span<int> second)
    {
        first = First;
        second = Second;
    }
}

[SkipSnapshotConsistency("Analyzer-only case")]
public static partial class CrossBarrierRefStructParameterKernels
{
    // --- Reported: the parameter is changed before a split point and read after it ---

    // GSH0039: the CPU dialects pass the original span again when the state machine resumes
    [GpuDeviceFunction]
    public static int Tail(Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        return s[0];
    }

    // GSH0039: the same in a kernel
    [GpuKernel]
    public static void SlicedSpanKernel(Span<int> sp, int[] o)
    {
        int tid = ThreadIndex.X;
        sp = sp.Slice(1);
        Gpu.SyncThreads();
        o[tid] = sp[0];
    }

    // GSH0039: the slice after the barrier reaches the barrier of the next iteration through the back edge
    [GpuDeviceFunction]
    public static int SumOfHeads(ReadOnlySpan<int> rs, int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++)
        {
            sum += rs[0];
            Gpu.SyncThreads();
            rs = rs.Slice(1);
        }

        return sum;
    }

    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuDeviceFunction]
    public static void Advance(ref Span<int> s) => s = s.Slice(1);

    // GSH0039: a ref argument changes the parameter before a call that suspends
    [GpuDeviceFunction]
    public static int AdvanceThenBroadcast(Span<int> s)
    {
        Advance(ref s);
        int broadcast = Broadcast(ThreadIndex.X);
        return s[0] + broadcast;
    }

    [GpuDeviceFunction]
    public static SpanPair Pair(Span<int> first, Span<int> second) => new() { First = first, Second = second };

    // GSH0039: a conditional value is assigned to the parameter
    [GpuDeviceFunction]
    public static int SliceIf(Span<int> s, bool skip)
    {
        s = skip ? s.Slice(1) : s;
        Gpu.SyncThreads();
        return s[0];
    }

    // GSH0039: a deconstruction writes the parameter
    [GpuDeviceFunction]
    public static int SwapThenRead(Span<int> s, Span<int> other)
    {
        (s, other) = Pair(other, s);
        Gpu.SyncThreads();
        return s[0];
    }

    // GSH0039: a field of a ref struct parameter is part of its value
    [GpuDeviceFunction]
    public static int MoveCursor(ParameterCursor cursor)
    {
        cursor.Position++;
        Gpu.SyncThreads();
        return cursor.Position;
    }

    // GSH0039: a conditional value is assigned to a field of the parameter
    [GpuDeviceFunction]
    public static int PlaceCursor(ParameterCursor cursor, bool skip)
    {
        cursor.Position = skip ? 1 : 2;
        Gpu.SyncThreads();
        return cursor.Position;
    }

    // --- Not reported ---

    // Read only: the CPU dialects pass the same span again, which is the value the parameter still holds
    [GpuDeviceFunction]
    public static int ReadAcrossBarrier(Span<int> s)
    {
        s[0] = 1;
        Gpu.SyncThreads();
        return s[0] + s.Length;
    }

    // Changed only after the last split point
    [GpuDeviceFunction]
    public static int SliceAfterBarrier(Span<int> s)
    {
        Gpu.SyncThreads();
        s = s.Slice(1);
        return s[0];
    }

    // Changed before the split point but not read after it
    [GpuDeviceFunction]
    public static void SliceBeforeBarrierOnly(Span<int> s, int[] o)
    {
        s = s.Slice(1);
        o[0] = s[0];
        Gpu.SyncThreads();
        o[1] = 0;
    }

    // Assigned again after the split point before it is read
    [GpuDeviceFunction]
    public static int ReassignedAfterBarrier(Span<int> s, Span<int> other)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        s = other;
        return s[0];
    }

    // A ref parameter refers to the caller's variable, which the CPU dialects pass again
    [GpuDeviceFunction]
    public static int SliceByReference(ref Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        return s[0];
    }
}
