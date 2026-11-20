using System.Runtime.ExceptionServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Runtime.Aot;

internal sealed class ManagedStackThreadPool
{
    internal const int MaxIdleThreads = 1024;

    internal static readonly ManagedStackThreadPool Instance = new();

    private readonly object _gate = new();
    private readonly Stack<ManagedStackThread> _idle = new();
    private int _createdThreadCount;
    private int _absorbedInterruptCount;

    [ThreadStatic]
    private static ManagedStackThread[]? t_rentedBuffer;

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
    internal ManagedStackThread[] Rent(int count, bool nested)
    {
        ManagedStackThread[] workers;
        if (nested)
        {
            workers = new ManagedStackThread[count];
        }
        else
        {
            workers = t_rentedBuffer is { } buffer && buffer.Length >= count ? buffer : new ManagedStackThread[count];
            t_rentedBuffer = workers;
        }

        var fromPool = 0;
        lock (_gate)
        {
            while (fromPool < count && _idle.Count > 0)
            {
                workers[fromPool++] = _idle.Pop();
            }
        }

        if (fromPool == count)
        {
            return workers;
        }

        var created = 0;
        try
        {
            for (var i = fromPool; i < count; i++)
            {
                var self = new ManagedStackThread();
                // [WHY] The default stack size of the platform, as the thread that ran the state machine had: a smaller
                // stack would turn a deep recursion that ran before into a StackOverflow, which no handler can catch
                var thread = new Thread(() => WorkerLoop(self))
                {
                    IsBackground = true,
                    Name = "G# managed GPU thread"
                };
                thread.UnsafeStart();
                workers[i] = self;
                created++;
            }

            lock (_gate)
            {
                _createdThreadCount += created;
            }

            return workers;
        }
        catch
        {
            // [WHY] The failure of the rent is what the caller sees. An interruption of the wait for the gate here is
            // not dropped either: the thread interrupts itself again, so that its next wait throws it, as .NET code does
            // with an interruption it cannot throw where it arrived
            if (ReturnTakingTheGate(workers, fromPool + created, created) is not null)
            {
                Thread.CurrentThread.Interrupt();
            }

            throw;
        }
    }

    internal void Return(ManagedStackThread[] workers, int count)
    {
        if (ReturnTakingTheGate(workers, count, created: 0) is { } interrupted)
        {
            ExceptionDispatchInfo.Throw(interrupted);
        }
    }

    // Returns <count> workers, and counts <created> threads that Rent started for them, and gives back the interruption
    // that ended a wait for the gate, for the caller to throw once the workers are back.
    // [WHY] A contended wait for the gate throws ThreadInterruptedException without the gate. Thrown from here, it
    // would leave the workers out of the pool, waiting forever for a launch, and the next launch would start new
    // threads. So the wait is taken again until it gets the gate, as ManagedBaton.HandOver throws only once the two
    // threads are in step. Every later interruption is waited through as well, since any number of them can come: the
    // first is the one thrown, and the later ones ask for the same
    private ThreadInterruptedException? ReturnTakingTheGate(ManagedStackThread[] workers, int count, int created)
    {
        ThreadInterruptedException? interrupted = null;
        while (true)
        {
            try
            {
                lock (_gate)
                {
                    _createdThreadCount += created;
                    PushIdle(workers, count);
                }

                break;
            }
            catch (ThreadInterruptedException ex)
            {
                // Only the wait for the gate throws it: the body of the lock does not wait
                interrupted ??= ex;
            }
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
            // (ManagedStackRunner.CancelWaiting)
            var w = workers[i];
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
                }
                catch (ManagedLaunchCancelledException)
                {
                    // The launch ended while this thread waited: the stack is unwound, nothing to report
                    self.Failure = null;
                }
                catch (Exception ex)
                {
                    self.Failure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    ManagedGpuContext.UnbindStackThread();
                }
            }

            // Answers the scheduler; the wait for the next wake is the one at the top of the loop
            self.Done = true;
            launch.Scheduler.Set();
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
                self.Go.Wait();
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
}
