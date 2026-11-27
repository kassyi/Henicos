using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Core;

#pragma warning disable SA1402 // Internal stack-thread transport types live together for Core/Runtime visibility
// One OS thread that runs one GPU thread of a block at a time. Only the scheduler and the thread itself touch it, and
// never at the same time: every field is written before the baton is handed over and read after it comes back.
// No volatile or lock is needed on these properties because the signal handoff of ManagedWorkerSignal establishes full
// memory barriers at every baton pass.
internal sealed class ManagedStackThread(bool useKernelSignalAndWait = false)
{
    internal ManagedWorkerSignal Go { get; } = new(useKernelSignalAndWait);

    internal ManagedStackLaunch? Launch { get; set; }

    internal Action<long>? Body { get; set; }

    internal long LinearThreadId { get; set; }

    internal int ThreadIndexInBlock { get; set; }

    internal GpuThreadContext Context { get; set; }

    internal bool AtBlockBarrier { get; set; }

    internal bool AtGridSync { get; set; }

    // Waits at a warp collective until the scheduler finds the lanes of its mask met there (WarpCanRun)
    internal bool AtWarp { get; set; }

    internal bool Done { get; set; }

    internal bool Retire { get; set; }

    internal ExceptionDispatchInfo? Failure { get; set; }

    // The visit counts of the warp collectives of the kernel this OS thread runs (ManagedGpuContext.StackWarp.cs):
    // grown when a kernel needs more, and kept for the next GPU threads and launches of the thread
    internal uint[] WarpVisits { get; set; } = [];

    // The counts of WarpVisits that the kernel of the current GPU thread cleared (begin); 0 until it does
    internal int WarpVisitCount { get; set; }
}

// What the threads of one launch share with their scheduler.
internal sealed class ManagedStackLaunch
{
    internal ManagedBatonSignal Scheduler { get; } = new();

    internal ManagedBlockState? State { get; set; }

    internal bool Cancelled { get; set; }

    // Whether the kernel reaches a warp collective: the scheduler then runs the rounds of the warp rendezvous
    internal bool ReachesWarpCollective { get; set; }

    // Whether the kernel is [GpuKernel(Cooperative = true)]: the scheduler runs all blocks in grid phases
    internal bool Cooperative { get; set; }

    internal string KernelName { get; set; } = string.Empty;

    internal ManagedStackThread[]? Workers { get; private set; }

    internal int WorkerOffset { get; private set; }

    internal int ThreadsPerBlock { get; private set; }

    internal int[]? RoundOrder { get; private set; }

    internal int Round { get; private set; }

    internal int CurrentOrderIndex { get; set; }

    internal ManagedStackThread? CurrentWorker { get; set; }

    internal bool AnyActive { get; set; }

    internal bool Stepped { get; set; }

    // Counted baton hand-overs during this launch, for runtime tests
    internal long HandOverCount { get; set; }

    internal int WakeAheadCount { get; set; }

    internal int WorkerSpinCount { get; set; }

    // Counted wake-ahead pulses sent during this launch, for runtime tests
    internal long WakeAheadSignalCount { get; set; }

    internal void BeginRound(
        ManagedStackThread[] workers,
        int workerOffset,
        int threadsPerBlock,
        int[]? order,
        int round)
    {
        Workers = workers;
        WorkerOffset = workerOffset;
        ThreadsPerBlock = threadsPerBlock;
        RoundOrder = order;
        Round = round;
        CurrentOrderIndex = -1;
        CurrentWorker = null;
        AnyActive = false;
        Stepped = false;
    }

    // Wakes up to WakeAheadCount runnable candidates after <currentOrderIndex> at the start of a round (or after a
    // mid-round failure resume) without incrementing _sent, so workers are ready in user space before the baton arrives.
    internal void PrimeWakeAhead(int currentOrderIndex)
    {
        if (WakeAheadCount <= 0 || Workers is not { } workers)
        {
            return;
        }

        var offset = WorkerOffset;
        var count = ThreadsPerBlock;
        var order = RoundOrder;
        var targetCount = WakeAheadCount;
        var woken = 0;

        for (var k = currentOrderIndex + 1; k < count && woken < targetCount; k++)
        {
            var t = order is null ? k : order[k];
            var w = workers[offset + t];
            if (w.Done || w.AtBlockBarrier || w.AtGridSync)
            {
                continue;
            }

            w.Go.WakeAhead();
            WakeAheadSignalCount++;
            woken++;
        }
    }

    // Wakes the WakeAheadCount-th non-done/non-barrier candidate after <startOrderIndex> within the current round,
    // without wrapping into already-finished threads at the end of the round.
    internal void WakeAheadAtOffset(int startOrderIndex)
    {
        if (WakeAheadCount <= 0 || Workers is not { } workers)
        {
            return;
        }

        var offset = WorkerOffset;
        var count = ThreadsPerBlock;
        var order = RoundOrder;
        var target = WakeAheadCount;
        var seen = 0;

        for (var k = startOrderIndex + 1; k < count; k++)
        {
            var t = order is null ? k : order[k];
            var w = workers[offset + t];
            if (w.Done || w.AtBlockBarrier || w.AtGridSync)
            {
                continue;
            }

            seen++;
            if (seen == target)
            {
                w.Go.WakeAhead();
                WakeAheadSignalCount++;
                return;
            }
        }
    }

    // Returns the order index >= <startOrderIndex> of the next thread that can run in the active round, or -1 when
    // the round is complete. Shared between the scheduler and direct thread hand-overs so both follow the exact same
    // round order and warp rendezvous rule.
    internal int FindNextRunnable(int startOrderIndex, out ManagedStackThread? nextWorker)
    {
        if (Workers is not { } workers)
        {
            nextWorker = null;
            return -1;
        }

        var offset = WorkerOffset;
        var count = ThreadsPerBlock;
        var order = RoundOrder;
        var warp = ReachesWarpCollective;
        var round = Round;

        for (var k = startOrderIndex; k < count; k++)
        {
            var t = order is null ? k : order[k];
            var w = workers[offset + t];
            if (w.Done || w.AtBlockBarrier || w.AtGridSync)
            {
                continue;
            }

            if (warp && !ManagedGpuContext.WarpCanRun(t, round))
            {
                AnyActive = true;
                continue;
            }

            nextWorker = w;
            return k;
        }

        nextWorker = null;
        return -1;
    }

    // Selects the next thread of the active round when <self> yields or exits, advancing CurrentOrderIndex and
    // CurrentWorker so the scheduler knows which worker owns the baton.
    internal ManagedStackThread? PrepareNextHandOver(ManagedStackThread self, bool selfDone)
    {
        AnyActive |= !selfDone && !self.AtBlockBarrier && !self.AtGridSync;
        var nextIndex = FindNextRunnable(CurrentOrderIndex + 1, out var nextWorker);
        if (nextWorker is not null)
        {
            CurrentOrderIndex = nextIndex;
            CurrentWorker = nextWorker;
            Stepped = true;
        }

        return nextWorker;
    }
}

internal static class ManagedBaton
{
    // Hands the baton directly from <self> to the next runnable worker of the round, or back to the launch scheduler
    // when the round is complete. Any wake-ahead pulse is sent before signaling <nextWorker.Go> so no two threads wake
    // concurrently.
    internal static void HandOverNext(ManagedStackThread self, ManagedStackLaunch launch)
    {
        var nextWorker = launch.PrepareNextHandOver(self, selfDone: false);
        if (nextWorker is not null)
        {
            launch.WakeAheadAtOffset(launch.CurrentOrderIndex);
        }

        launch.HandOverCount++;
        if (nextWorker is not null)
        {
            HandOver(nextWorker.Go, self.Go, launch.WorkerSpinCount);
        }
        else
        {
            HandOver(launch.Scheduler, self.Go, launch.WorkerSpinCount);
        }
    }

    // Wakes the owner of <next> and waits for <own>: the caller stops running here.
    // [WHY] Not WaitHandle.SignalAndWait on the scheduler when the launching thread is STA or carries a
    // SynchronizationContext: SignalAndWait throws NotSupportedException on an STA thread and bypasses
    // SynchronizationContext.Wait. When the launching thread is MTA without a SynchronizationContext and the worker
    // uses an OS AutoResetEvent, SignalAndWait hands over in a single kernel call.
    // [WHY] A wait that ends with an exception (a Thread.Interrupt, or a SynchronizationContext that asks for wait
    // notification and throws from its Wait, for example from a launch it ran there) would leave <next> running while
    // the caller runs too, and the reply would stay pending on <own> for a later wait: the caller then reads state the
    // other thread is writing, and a later baton wakes the wrong thread. So the caller makes sure the reply has come
    // (WaitAfterFailure), and the exception is thrown after it, when the two threads are in step again. Also,
    // SignalAndWait throws ThreadInterruptedException before signaling <next> when an interrupt is already pending, so
    // the catch handler pulses <next>'s handle without advancing _sent again before calling WaitAfterFailure.
    internal static void HandOver(ManagedWorkerSignal next, ManagedBatonSignal own)
    {
        if (next.KernelHandle is { } nextHandle &&
            !own.HasPendingSignal &&
            SynchronizationContext.Current is null &&
            Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            next.RecordSet();
            try
            {
                WaitHandle.SignalAndWait(nextHandle, own.Handle);
                own.CompleteWait();
                return;
            }
            catch (Exception)
            {
                next.PulseHandleOnly();
                own.WaitAfterFailure();
                throw;
            }
        }

        next.Set();
        try
        {
            own.Wait();
        }
        catch (Exception)
        {
            // Thrown again once the reply has come: see [WHY] above
            own.WaitAfterFailure();
            throw;
        }
    }

    internal static void HandOver(ManagedWorkerSignal next, ManagedWorkerSignal own, int spin)
    {
        if (next.KernelHandle is { } nextHandle &&
            own.KernelHandle is { } ownHandle &&
            !own.HasPendingSignal)
        {
            next.RecordSet();
            try
            {
                WaitHandle.SignalAndWait(nextHandle, ownHandle);
                own.CompleteWait();
                return;
            }
            catch (Exception)
            {
                next.PulseHandleOnly();
                own.WaitAfterFailure();
                throw;
            }
        }

        next.Set();
        try
        {
            own.Wait(spin);
        }
        catch (Exception)
        {
            own.WaitAfterFailure();
            throw;
        }
    }

    internal static void HandOver(ManagedBatonSignal next, ManagedWorkerSignal own, int spin)
    {
        if (own.KernelHandle is { } ownHandle && !own.HasPendingSignal)
        {
            next.RecordSet();
            try
            {
                WaitHandle.SignalAndWait(next.Handle, ownHandle);
                own.CompleteWait();
                return;
            }
            catch (Exception)
            {
                next.PulseHandleOnly();
                own.WaitAfterFailure();
                throw;
            }
        }

        next.Set();
        try
        {
            own.Wait(spin);
        }
        catch (Exception)
        {
            own.WaitAfterFailure();
            throw;
        }
    }
}

// The signal one thread of the stack model waits for. The count of the signals sent and of those its thread has taken
// is the record; the event only wakes the thread. A wait that failed can then tell from the counts whether its signal
// has come (a SynchronizationContext may take the event and throw after it), and a set of the event that no count goes
// with (left over after such a failure, or not taken while an interruption was pending) only makes the thread look at
// the counts again. Only one thread waits for a signal; any thread may send one.
internal sealed class ManagedBatonSignal
{
    private readonly AutoResetEvent _event = new(false);
    private long _sent;
    private long _taken;

    // The interruptions that arrived while a failed wait waited for its signal; see WaitAfterFailure
    internal int DiscardedInterrupts { get; private set; }

    internal WaitHandle Handle => _event;

    internal bool HasPendingSignal => Volatile.Read(ref _sent) > _taken;

    internal void RecordSet() => Interlocked.Increment(ref _sent);

    internal void PulseHandleOnly() => _event.Set();

    internal void CompleteWait()
    {
        while (Volatile.Read(ref _sent) <= _taken)
        {
            _event.WaitOne();
        }

        _taken++;
    }

    // [WHY] The count goes up before the event is set: a waiter that the event wakes always finds the signal counted.
    // In the other order it could wake, find no new count, wait again and miss the set it was woken by.
    internal void Set()
    {
        Interlocked.Increment(ref _sent);
        _event.Set();
    }

    // Waits for the next signal. A wait that throws has not counted it: see WaitAfterFailure
    internal void Wait()
    {
        while (Volatile.Read(ref _sent) <= _taken)
        {
            _event.WaitOne();
        }

        _taken++;
    }

    // After a failed Wait: waits until the signal of that wait has come and counts it. The waits here do not go through
    // the SynchronizationContext, which may fail every wait.
    internal void WaitAfterFailure()
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            while (Volatile.Read(ref _sent) <= _taken)
            {
                try
                {
                    _event.WaitOne();
                }
                catch (ThreadInterruptedException)
                {
                    // [WHY] The thread already ends this wait with the exception of its first failure: a further
                    // interruption asks for the same, so it is counted and dropped. Raising it again would fail the
                    // next, unrelated wait of the thread (or a lock) after the launch it was meant for has ended.
                    DiscardedInterrupts++;
                }
            }

            _taken++;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }
}

// Worker-side signal supporting either kernel SignalAndWait (AutoResetEvent) or user-mode ManualResetEventSlim with
// wake-ahead and bounded spinning. Only one worker waits on this signal; any thread may send one.
internal sealed class ManagedWorkerSignal
{
    private readonly AutoResetEvent? _event;
    private readonly ManualResetEventSlim? _slim;
    private long _sent;
    private long _taken;
    private volatile int _spinning;

    internal ManagedWorkerSignal(bool useKernelSignalAndWait)
    {
        if (useKernelSignalAndWait)
        {
            _event = new AutoResetEvent(false);
        }
        else
        {
            _slim = new ManualResetEventSlim(false, 0);
        }
    }

    internal bool UsesKernelSignalAndWait => _event is not null;

    internal WaitHandle? KernelHandle => _event;

    internal bool HasPendingSignal => Volatile.Read(ref _sent) > _taken;

    internal void RecordSet() => Interlocked.Increment(ref _sent);

    internal void PulseHandleOnly() => _event?.Set();

    internal void Set()
    {
        Interlocked.Increment(ref _sent);
        if (_event is not null)
        {
            _event.Set();
            return;
        }

        if (_spinning == 0)
        {
            _slim?.Set();
        }
    }

    // Wakes the worker without advancing _sent so execution order stays strictly controlled by counted batons.
    internal void WakeAhead() => _slim?.Set();

    internal void Wait(int spin = 0)
    {
        while (Volatile.Read(ref _sent) <= _taken)
        {
            Park();
            if (_slim is not null && spin > 0 && Volatile.Read(ref _sent) <= _taken)
            {
                SpinOnce(spin);
            }
        }

        _taken++;
    }

    // [WHY] Reads self.Done and self.Launch after Park() returns so a worker parked idle in the pool (Launch == null,
    // Done == true) does not spin while idle, yet still spins if woken ahead after AssignBlock attaches it to a launch.
    internal void WaitOutsideKernel(ManagedStackThread self)
    {
        while (Volatile.Read(ref _sent) <= _taken)
        {
            Park();
            var spin = _slim is not null && !self.Done && self.Launch is { } launch ? launch.WorkerSpinCount : 0;
            if (spin > 0 && Volatile.Read(ref _sent) <= _taken)
            {
                SpinOnce(spin);
            }
        }

        _taken++;
    }

    internal void CompleteWait()
    {
        while (Volatile.Read(ref _sent) <= _taken)
        {
            _event?.WaitOne();
        }

        _taken++;
    }

    internal void WaitAfterFailure()
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            while (Volatile.Read(ref _sent) <= _taken)
            {
                try
                {
                    Park();
                }
                catch (ThreadInterruptedException)
                {
                    // Drop extra interruptions once a failure is already on its way, as ManagedBatonSignal does.
                }
            }

            _taken++;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private void Park()
    {
        if (_event is not null)
        {
            _event.WaitOne();
            return;
        }

        if (_slim is not null)
        {
            _slim.Wait();
            _slim.Reset();
        }
    }

    private void SpinOnce(int spin)
    {
        _spinning = 1;
        Interlocked.MemoryBarrier();
        for (var i = 0; i < spin && Volatile.Read(ref _sent) <= _taken; i++)
        {
            Thread.SpinWait(8);
        }

        _spinning = 0;
        Interlocked.MemoryBarrier();
    }
}

internal sealed class ManagedLaunchCancelledException()
    : Exception("G#: the launch ended while this GPU thread waited at a block barrier or a warp collective");
#pragma warning restore SA1402
