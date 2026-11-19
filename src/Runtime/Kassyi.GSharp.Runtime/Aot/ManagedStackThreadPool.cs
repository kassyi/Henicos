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
            if (created > 0)
            {
                lock (_gate)
                {
                    _createdThreadCount += created;
                }
            }

            Return(workers, fromPool + created);
            throw;
        }
    }

    internal void Return(ManagedStackThread[] workers, int count)
    {
        lock (_gate)
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

        Array.Clear(workers, 0, count);
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
}
