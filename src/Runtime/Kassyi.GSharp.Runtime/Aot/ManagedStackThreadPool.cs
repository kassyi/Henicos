using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Runtime.Aot;

internal sealed class ManagedStackThreadPool
{
    internal const int MaxIdleThreads = 1024;
    private const int ParallelThreadStartThreshold = 16;

    internal static readonly ManagedStackThreadPool Instance = new();

    private readonly object _gate = new();
    private readonly Stack<ManagedStackThread> _idle = new();
    private int _createdThreadCount;
    private int _absorbedInterruptCount;

    [ThreadStatic]
    private static ManagedStackThread[]? t_rentedBuffer;

    [ThreadStatic]
    private static Action? t_threadsStartedForTest;

    [ThreadStatic]
    private static Action<int>? t_beforeThreadStartForTest;

    internal int CreatedThreadCount
    {
        get
        {
            lock (_gate)
            {
                return _createdThreadCount;
            }
        }
    }

    // The interruptions that pooled threads received outside a kernel, where no launch can fail (see WaitOutsideKernel)
    internal int AbsorbedInterruptCount => Volatile.Read(ref _absorbedInterruptCount);

    internal int IdleThreadCount
    {
        get
        {
            lock (_gate)
            {
                return _idle.Count;
            }
        }
    }

    // <nested>: the calling thread rents while an earlier launch of its own still holds the thread's buffer (see
    // ManagedStackRunner.Run), so the workers go into a new array
    internal ManagedStackThread[] Rent(int count, bool nested, bool useKernelSignalAndWait = false)
    {
        if (!TryRent(count, nested, useKernelSignalAndWait, out var workers, out _, out var failure))
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        return workers;
    }

    // Like Rent, but returns false with the number of workers already obtained/started when starting an OS thread fails
    // with OutOfMemoryException or ThreadStartException (the obtained workers are already returned to the pool)
    internal bool TryRent(int count, bool nested, bool useKernelSignalAndWait, out ManagedStackThread[] workers,
        out int startedCount, [NotNullWhen(false)] out Exception? failure)
    {
        var rented = AllocateWorkers(count, nested);
        var fromPool = PopIdle(rented, count, useKernelSignalAndWait);
        if (fromPool == count)
        {
            workers = rented;
            startedCount = count;
            failure = null;
            return true;
        }

        var created = 0;
        try
        {
            StartThreads(rented, fromPool, count, useKernelSignalAndWait, ref created);
            t_threadsStartedForTest?.Invoke();
            CountCreated(created);
            workers = rented;
            startedCount = count;
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ThreadStartException)
        {
            // [WHY] The resource failure of the rent is what the caller wraps; an interruption of the wait for the
            // gate here comes while that failure is already on its way, and is dropped (see ReturnTakingTheGate).
            // Scan the full [0..count) range because a parallel start failure can leave non-contiguous null slots.
            ReturnTakingTheGate(rented, count, created);
            workers = [];
            startedCount = fromPool + created;
            failure = ex;
            return false;
        }
        catch
        {
            ReturnTakingTheGate(rented, count, created);
            throw;
        }
    }

    private static ManagedStackThread[] AllocateWorkers(int count, bool nested)
    {
        if (nested)
        {
            return new ManagedStackThread[count];
        }

        var workers = t_rentedBuffer is { } buffer && buffer.Length >= count ? buffer : new ManagedStackThread[count];
        t_rentedBuffer = workers;
        return workers;
    }

    // Takes up to <count> idle threads matching <useKernelSignalAndWait> into workers[0..]; any mismatched idle
    // threads (for example when a test switches wake-ahead mode) are retired safely under the gate.
    private int PopIdle(ManagedStackThread[] workers, int count, bool useKernelSignalAndWait)
    {
        var fromPool = 0;
        EnterGate();
        try
        {
            while (fromPool < count && _idle.Count > 0)
            {
                var w = _idle.Pop();
                if (w.Go.UsesKernelSignalAndWait != useKernelSignalAndWait)
                {
                    w.Retire = true;
                    w.Go.Set();
                    continue;
                }

                workers[fromPool++] = w;
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        return fromPool;
    }

    private int PopAnyIdle(ManagedStackThread[] workers, int count)
    {
        var fromPool = 0;
        EnterGate();
        try
        {
            while (fromPool < count && _idle.Count > 0)
            {
                workers[fromPool++] = _idle.Pop();
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        return fromPool;
    }

    private void CountCreated(int created)
    {
        EnterGate();
        try
        {
            _createdThreadCount += created;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    // For tests only, per thread: runs on the thread that set it, only when its Rent starts new threads, after they
    // have started and before they are counted
    internal static Action? ThreadsStartedForTest
    {
        get => t_threadsStartedForTest;
        set => t_threadsStartedForTest = value;
    }

    // For tests only, per thread: runs immediately before starting each new OS thread, passed the number of workers
    // obtained/started so far
    internal static Action<int>? BeforeThreadStartForTest
    {
        get => t_beforeThreadStartForTest;
        set => t_beforeThreadStartForTest = value;
    }

    internal void Return(ManagedStackThread[] workers, int count)
    {
        if (ReturnTakingTheGate(workers, count, created: 0) is { } interrupted)
        {
            ExceptionDispatchInfo.Throw(interrupted);
        }
    }

    // Starts the threads of workers[<from>..<count>), counting each one that has started in <created>
    private static void StartThreads(ManagedStackThread[] workers, int from, int count, bool useKernelSignalAndWait,
        ref int created)
    {
        var missing = count - from;
        if (missing >= ParallelThreadStartThreshold && t_beforeThreadStartForTest is null)
        {
            StartThreadsParallel(workers, from, count, useKernelSignalAndWait, ref created);
            return;
        }

        for (var i = from; i < count; i++)
        {
            t_beforeThreadStartForTest?.Invoke(i);
            workers[i] = StartSingleThread(useKernelSignalAndWait);
            created++;
        }
    }

    private static void StartThreadsParallel(ManagedStackThread[] workers, int from, int count,
        bool useKernelSignalAndWait, ref int created)
    {
        var missing = count - from;
        var workerCount = Math.Min(Environment.ProcessorCount, Math.Max(1, missing / 8));
        if (workerCount <= 1)
        {
            for (var i = from; i < count; i++)
            {
                workers[i] = StartSingleThread(useKernelSignalAndWait);
                created++;
            }

            return;
        }

        var chunk = (missing + workerCount - 1) / workerCount;
        var tasks = new Task[workerCount];
        var sharedCreated = 0;
        ExceptionDispatchInfo? firstFailure = null;
        for (var p = 0; p < workerCount; p++)
        {
            var sliceStart = from + (p * chunk);
            var sliceEnd = Math.Min(count, sliceStart + chunk);
            tasks[p] = Task.Run(() =>
            {
                for (var i = sliceStart; i < sliceEnd; i++)
                {
                    if (Volatile.Read(ref firstFailure) is not null)
                    {
                        return;
                    }

                    try
                    {
                        workers[i] = StartSingleThread(useKernelSignalAndWait);
                        Interlocked.Increment(ref sharedCreated);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(ex), null);
                        return;
                    }
                }
            });
        }

        Task.WaitAll(tasks);
        created += Volatile.Read(ref sharedCreated);
        firstFailure?.Throw();
    }

    private static ManagedStackThread StartSingleThread(bool useKernelSignalAndWait)
    {
        var self = new ManagedStackThread(useKernelSignalAndWait);
        // [WHY] The default stack size of the platform, as the thread that ran the state machine had: a smaller
        // stack would turn a deep recursion that ran before into a StackOverflow, which no handler can catch
        var thread = new Thread(() => WorkerLoop(self))
        {
            IsBackground = true,
            Name = "G# managed GPU thread"
        };
        thread.UnsafeStart();
        return self;
    }

    // [WHY] A contended wait for the gate is a wait of the thread: it goes through a SynchronizationContext that asks
    // for wait notification (as a UI dispatcher does), and that context may fail the wait with any exception. The
    // context is cleared for the wait, as ManagedBatonSignal.WaitAfterFailure does, so that only an interruption can
    // end it without the gate
    private void EnterGate()
    {
        var context = SynchronizationContext.Current;
        if (context is null)
        {
            Monitor.Enter(_gate);
            return;
        }

        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Monitor.Enter(_gate);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    // Returns <count> workers, and counts <created> threads that Rent started for them, and gives back the interruption
    // that ended a wait for the gate, for the caller to throw once the workers are back.
    // [WHY] A contended wait for the gate throws ThreadInterruptedException without the gate. Thrown from here, it
    // would leave the workers out of the pool, waiting forever for a launch, and the next launch would start new
    // threads. So the wait is taken again until it gets the gate, as ManagedBaton.HandOver throws only once the two
    // threads are in step. The policy for the interruptions, the same as ManagedBatonSignal.WaitAfterFailure: one
    // exception per failure of the thread. Return throws the first interruption, and the later ones ask for the same
    // and are dropped; Rent, already failing, drops them all. Raising one again would fail the next, unrelated wait of
    // the thread after the launch it was meant for has ended
    private ThreadInterruptedException? ReturnTakingTheGate(ManagedStackThread[] workers, int count, int created)
    {
        ThreadInterruptedException? interrupted = null;
        while (true)
        {
            try
            {
                EnterGate();
                break;
            }
            catch (ThreadInterruptedException ex)
            {
                interrupted ??= ex;
            }
        }

        // The body does not wait, so no interruption can stop it halfway
        try
        {
            _createdThreadCount += created;
            PushIdle(workers, count);
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        Array.Clear(workers, 0, count);
        return interrupted;
    }

    // Under the gate
    private void PushIdle(ManagedStackThread[] workers, int count)
    {
        for (var i = 0; i < count; i++)
        {
            // Every worker has left the kernel: the launch unwinds them all before it returns them
            // (ManagedStackRunner.CancelWaiting); parallel Rent failure can leave null slots.
            if (workers[i] is not { } w)
            {
                continue;
            }

            w.Launch = null;
            w.Body = null;
            w.Failure = null;
            w.Context = default;
            if (_idle.Count < MaxIdleThreads)
            {
                _idle.Push(w);
            }
            else
            {
                w.Retire = true;
                w.Go.Set();
            }
        }
    }

    // For tests only: holds the gate until the scope is disposed, on the thread that took it, so that a test can make
    // Rent or Return wait for the gate
    internal IDisposable HoldGateForTest()
    {
        Monitor.Enter(_gate);
        return new GateScope(_gate);
    }

    // For tests only: takes every idle thread out of the pool until the scope is disposed, so that a launch has to
    // start its threads whatever the pool held
    internal IDisposable BorrowIdleThreadsForTest()
    {
        var workers = new ManagedStackThread[IdleThreadCount];
        return new BorrowScope(this, workers, PopAnyIdle(workers, workers.Length));
    }

    private static void WorkerLoop(ManagedStackThread self)
    {
        while (true)
        {
            Instance.WaitOutsideKernel(self);
            if (self.Retire)
            {
                return;
            }

            // [WHY] Only a launch wakes a worker that is not retiring, and it sets Launch first; a wake without one
            // means that the baton of the stack model lost step, an error of G# that must not pass unnoticed
            var launch = self.Launch ?? throw new InvalidOperationException(
                "G#: internal error: a stack thread was woken without a launch (the managed stack model lost step)");
            ManagedStackThread? nextWorker = null;
            if (!launch.Cancelled)
            {
                try
                {
                    ManagedGpuContext.BindStackThread(self);
                    var body = self.Body ??
                        throw new InvalidOperationException("G#: a stack thread was woken without a body");
                    body(self.LinearThreadId);
                    // [WHY] An interruption of this GPU thread that no wait of the kernel took (Thread.Interrupt of the
                    // running thread) belongs to the kernel: Sleep(0) throws it here, so that it fails this launch
                    // instead of a wait between launches
                    Thread.Sleep(0);
                    if (!launch.Cancelled)
                    {
                        if (launch.ReachesWarpCollective)
                        {
                            ManagedGpuContext.MarkWarpLaneExited(self.ThreadIndexInBlock);
                        }

                        nextWorker = launch.PrepareNextHandOver(self, selfDone: true);
                    }
                }
                catch (ManagedLaunchCancelledException)
                {
                    // The launch ended while this thread waited: the stack is unwound, nothing to report
                    self.Failure = null;
                    nextWorker = null;
                }
                catch (Exception ex)
                {
                    self.Failure = ExceptionDispatchInfo.Capture(ex);
                    nextWorker = null;
                }
                finally
                {
                    ManagedGpuContext.UnbindStackThread();
                }
            }

            // Answers the next runnable thread of the round or the scheduler; the wait for the next wake is at the top of the loop
            self.Done = true;
            launch.HandOverCount++;
            if (nextWorker is not null)
            {
                launch.WakeAheadAtOffset(launch.CurrentOrderIndex);
                nextWorker.Go.Set();
            }
            else
            {
                launch.Scheduler.Set();
            }
        }
    }

    // [WHY] Outside a kernel (in the pool, or after the last answer of a launch) there is no launch to fail: an
    // interruption that arrives here is counted and the thread keeps waiting. Letting it escape would end the process,
    // and ending the thread would lose a worker that the pool still lists.
    private void WaitOutsideKernel(ManagedStackThread self)
    {
        while (true)
        {
            try
            {
                self.Go.WaitOutsideKernel(self);
                return;
            }
            catch (ThreadInterruptedException)
            {
                Interlocked.Increment(ref _absorbedInterruptCount);
            }
        }
    }

    private sealed class GateScope(object gate) : IDisposable
    {
        public void Dispose() => Monitor.Exit(gate);
    }

    private sealed class BorrowScope(ManagedStackThreadPool pool, ManagedStackThread[] workers, int count) : IDisposable
    {
        public void Dispose() => pool.Return(workers, count);
    }
}
