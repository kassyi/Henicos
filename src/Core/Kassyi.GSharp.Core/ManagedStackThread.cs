using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Core;

#pragma warning disable SA1402 // Internal stack-thread transport types live together for Core/Runtime visibility
// One OS thread that runs one GPU thread of a block at a time. Only the scheduler and the thread itself touch it, and
// never at the same time: every field is written before the baton is handed over and read after it comes back.
// No volatile or lock is needed on these properties because the AutoResetEvent.Set and WaitOne of ManagedBatonSignal
// establish full memory barriers at every handoff.
internal sealed class ManagedStackThread
{
    internal ManagedBatonSignal Go { get; } = new();

    internal ManagedStackLaunch? Launch { get; set; }

    internal Action<long>? Body { get; set; }

    internal long LinearThreadId { get; set; }

    internal GpuThreadContext Context { get; set; }

    internal bool AtBlockBarrier { get; set; }

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
}

internal static class ManagedBaton
{
    // Wakes the owner of <next> and waits for <own>: the caller stops running here.
    // [WHY] Not WaitHandle.SignalAndWait: it throws NotSupportedException on an STA thread, and the scheduler runs on
    // the thread that launched the kernel, which may be a UI thread. The two steps need not be atomic: AutoResetEvent
    // keeps one pending signal, so a reply that comes before the wait starts is not lost.
    // [WHY] A wait that ends with an exception (a Thread.Interrupt, or a SynchronizationContext that asks for wait
    // notification and throws from its Wait, for example from a launch it ran there) would leave <next> running while
    // the caller runs too, and the reply would stay pending on <own> for a later wait: the caller then reads state the
    // other thread is writing, and a later baton wakes the wrong thread. So the caller makes sure the reply has come
    // (ManagedBatonSignal.WaitAfterFailure), and the exception is thrown after it, when the two threads are in step
    // again.
    internal static void HandOver(ManagedBatonSignal next, ManagedBatonSignal own)
    {
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

internal sealed class ManagedLaunchCancelledException()
    : Exception("G#: the launch ended while this GPU thread waited at a block barrier or a warp collective");
#pragma warning restore SA1402
