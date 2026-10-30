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

    // --- Reported: the lowering reads the receiver after the call that suspends in its argument ---

    // GSH0039: Broadcast runs first, then the indexer reads the parameter, which is the original span again
    [GpuDeviceFunction]
    public static int IndexBySuspendingCall(Span<int> s)
    {
        s = s.Slice(1);
        return s[Broadcast(0)];
    }

    // GSH0039: the receiver of Slice is read after Broadcast, before the assignment writes the parameter
    [GpuDeviceFunction]
    public static int SliceBySuspendingCall(Span<int> s)
    {
        s = s.Slice(1);
        s = s.Slice(Broadcast(0));
        return s[0];
    }

    [GpuDeviceFunction]
    public static int HeadAfterBarrier(Span<int> x)
    {
        Gpu.SyncThreads();
        return x[0];
    }

    // GSH0039: every resume runs the call again with its identifier arguments, here the original span
    [GpuDeviceFunction]
    public static int PassToSuspendingCall(Span<int> s)
    {
        s = s.Slice(1);
        return HeadAfterBarrier(s);
    }

    // GSH0039: a local span read by the indexer after Broadcast would have to be kept in the frame
    [GpuDeviceFunction]
    public static int IndexLocalBySuspendingCall(Span<int> s)
    {
        Span<int> tail = s.Slice(1);
        return tail[Broadcast(0)];
    }

    // --- Reported: a property changes the parameter ---

    // GSH0039: a property setter
    [GpuDeviceFunction]
    public static int SetProperty(PropertyCursor cursor)
    {
        cursor.Value = 1;
        Gpu.SyncThreads();
        return cursor.Value;
    }

    // GSH0039: an increment calls the setter
    [GpuDeviceFunction]
    public static int IncrementProperty(PropertyCursor cursor)
    {
        cursor.Value++;
        Gpu.SyncThreads();
        return cursor.Value;
    }

    // GSH0039: a conditional value is assigned through the setter
    [GpuDeviceFunction]
    public static int SetPropertyIf(PropertyCursor cursor, bool skip)
    {
        cursor.Value = skip ? 1 : 2;
        Gpu.SyncThreads();
        return cursor.Value;
    }

    // GSH0039: a getter that is not readonly may change the struct
    [GpuDeviceFunction]
    public static int ReadAdvancingProperty(PropertyCursor cursor)
    {
        int next = cursor.Next;
        Gpu.SyncThreads();
        return cursor.Value + next;
    }

    // GSH0039: rebinding a ref field changes the parameter
    [GpuDeviceFunction]
    public static int RebindRefField(RefFieldHolder holder)
    {
        holder.Target = ref holder.Other;
        Gpu.SyncThreads();
        return holder.Target;
    }

    // --- Not reported ---

    // The parameter is not changed, so the indexer reads the same span after Broadcast
    [GpuDeviceFunction]
    public static int IndexUnchangedBySuspendingCall(Span<int> s) => s[Broadcast(0)];

    // The lowering evaluates the argument s[0] once, before the call suspends
    [GpuDeviceFunction]
    public static int BroadcastHead(Span<int> s)
    {
        s = s.Slice(1);
        return Broadcast(s[0]);
    }

    // The shuffle publishes s[0] before it splits
    [GpuDeviceFunction]
    public static int ShuffleHead(Span<int> s)
    {
        s = s.Slice(1);
        return Warp.ShuffleSync(0xFFFFFFFFu, s[0], 0);
    }

    // A readonly getter does not change the struct
    [GpuDeviceFunction]
    public static int ReadProperty(PropertyCursor cursor)
    {
        int value = cursor.Value;
        Gpu.SyncThreads();
        return cursor.Value + value;
    }

    // Writing through a ref field changes the referenced variable, not the parameter
    [GpuDeviceFunction]
    public static int WriteThroughRefField(RefFieldHolder holder)
    {
        holder.Target = 1;
        Gpu.SyncThreads();
        return holder.Target;
    }

    // The same with an increment
    [GpuDeviceFunction]
    public static int IncrementThroughRefField(RefFieldHolder holder)
    {
        holder.Target++;
        Gpu.SyncThreads();
        return holder.Target;
    }

    // The same with a conditional value
    [GpuDeviceFunction]
    public static int WriteThroughRefFieldIf(RefFieldHolder holder, bool skip)
    {
        holder.Target = skip ? 1 : 2;
        Gpu.SyncThreads();
        return holder.Target;
    }
}

/// <summary>A ref struct with a readonly getter and a setter, and a getter that changes it.</summary>
public ref struct PropertyCursor
{
    public int Position;

    public int Value { readonly get => Position; set => Position = value; }

    public int Next => ++Position;
}

/// <summary>A ref struct whose ref fields refer to variables outside it.</summary>
public ref struct RefFieldHolder
{
    public ref int Target;
    public ref int Other;
}
