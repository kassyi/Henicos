#nullable enable
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using static Kassyi.GSharp.Tests.Cpu.Runtime.ManagedStackPoolContexts;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

// The launches that find the pool's gate held by another thread when they rent or return their threads, and whose wait
// for the gate is interrupted or fails: every thread must still go back to the pool
public partial class ManagedStackPoolTests
{
    private const string RentFailureMessage = "the rent failed";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_SchedulerInterruptedWhileWaitingForThePoolGate_ThrowsAndReturnsEveryThreadToThePool()
    {
        const int threads = 4;
        Thread? scheduler = null;
        var context = new CountingWaitContext();
        using var holdRequested = new ManualResetEventSlim();
        using var gateHeld = new ManualResetEventSlim();

        // The pool holds a block of idle threads, so that the launch below takes them instead of starting new ones
        Assert.Equal(ExpectedReverse(threads, 1), RunReverse(threads, 1));
        int createdBefore = ManagedStackRunner.CreatedThreadCount;
        int idleBefore = ManagedStackRunner.IdleThreadCount;
        int idleAfterInterrupt = -1;

        // The scheduler takes the last answer and blocks in Return's wait for the gate: it is interrupted there
        var holder = new GateHolder(holdRequested, gateHeld, () =>
        {
            Assert.True(SpinWait.SpinUntil(() => BlockedOutsideTheContext(scheduler, context), s_blockTimeout),
                "The scheduler did not block in its wait for the gate of the pool");
            InterruptAndWaitUntilThrown(scheduler);
        });

        var after = RunOnNewThread(() =>
        {
            scheduler = Thread.CurrentThread;
            Assert.Throws<ThreadInterruptedException>(() =>
                RunHoldingTheGateInTheLastThread(threads, context, holdRequested, gateHeld, armContext: false));
            idleAfterInterrupt = ManagedStackRunner.IdleThreadCount;
            return RunReverse(threads, 30);
        });

        holder.AssertEnded();
        Assert.Equal(idleBefore, idleAfterInterrupt);
        Assert.Equal(ExpectedReverse(threads, 30), after);
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_ContextFailsEveryWaitWhileThePoolGateIsHeld_EndsAndReturnsEveryThreadToThePool()
    {
        const int threads = 4;
        Thread? scheduler = null;
        var context = new CountingWaitContext();
        using var holdRequested = new ManualResetEventSlim();
        using var gateHeld = new ManualResetEventSlim();
        using var runEnded = new ManualResetEventSlim();

        Assert.Equal(ExpectedReverse(threads, 1), RunReverse(threads, 1));
        int createdBefore = ManagedStackRunner.CreatedThreadCount;
        int idleBefore = ManagedStackRunner.IdleThreadCount;
        int idleAfterLaunch = -1;

        // The gate goes once the scheduler blocks in Return's wait for it, or once the launch has ended (a wait for
        // the gate that went through the armed context fails at once)
        var holder = new GateHolder(holdRequested, gateHeld, () => Assert.True(
            SpinWait.SpinUntil(() => runEnded.IsSet || BlockedOutsideTheContext(scheduler, context), s_blockTimeout),
            "The scheduler did not block in its wait for the gate of the pool"));

        Exception? failure = null;
        var after = RunOnNewThread(() =>
        {
            scheduler = Thread.CurrentThread;
            try
            {
                failure = Record.Exception(() =>
                    RunHoldingTheGateInTheLastThread(threads, context, holdRequested, gateHeld, armContext: true));
            }
            finally
            {
                runEnded.Set();
            }

            idleAfterLaunch = ManagedStackRunner.IdleThreadCount;
            return RunReverse(threads, 30);
        });

        holder.AssertEnded();
        Assert.Null(failure);
        Assert.Equal(idleBefore, idleAfterLaunch);
        Assert.Equal(ExpectedReverse(threads, 30), after);
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(RentFailure.InterruptedWhileCounting)]
    [InlineData(RentFailure.InterruptionThenInterruptedWhileReturning)]
    [InlineData(RentFailure.FailureThenInterruptedWhileReturning)]
    public void Run_RentFailsAfterStartingThreads_ThrowsOnceAndPoolsEveryThread(RentFailure failure)
    {
        const int threads = 4;
        Assert.Equal(ExpectedReverse(threads, 1), RunReverse(threads, 1));
        var pool = ManagedStackThreadPool.Instance;
        int idleBefore = pool.IdleThreadCount;
        int createdBefore = pool.CreatedThreadCount;
        Thread? scheduler = null;
        using var holdRequested = new ManualResetEventSlim();
        using var gateHeld = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();

        // Once the rent has started its threads, its next wait is for the gate: the one that counts the threads, or,
        // after the failure in ThreadsStartedForTest, the one in its catch. That wait is interrupted
        var holder = new GateHolder(holdRequested, gateHeld, () =>
        {
            Assert.True(SpinWait.SpinUntil(() => started.IsSet && IsBlocked(scheduler), s_blockTimeout),
                "The scheduler did not block in its wait for the gate of the pool");
            InterruptAndWaitUntilThrown(scheduler);
        });

        // Every idle thread is out of the pool, so that the launch starts its threads
        var borrowed = pool.Rent(idleBefore, nested: true);
        RentOutcome outcome;
        int idleAfterLaunch;
        try
        {
            outcome = RunOnNewThread(() =>
            {
                scheduler = Thread.CurrentThread;
                return RunWithFailingRent(threads, failure, holdRequested, gateHeld, started);
            });
            idleAfterLaunch = pool.IdleThreadCount;
        }
        finally
        {
            pool.Return(borrowed, idleBefore);
        }

        holder.AssertEnded();
        Assert.False(outcome.KernelRan);
        Assert.IsType(failure == RentFailure.FailureThenInterruptedWhileReturning
            ? typeof(InvalidOperationException)
            : typeof(ThreadInterruptedException), outcome.Failure);
        // The interruption that came while the failure was on its way was dropped, not left for a later wait
        Assert.False(outcome.InterruptionPending);
        Assert.Equal(threads, idleAfterLaunch);
        Assert.Equal(createdBefore + threads, pool.CreatedThreadCount);
        AssertLaterLaunches(RunLaterLaunches(threads, 10), threads, 10);
        Assert.Equal(idleBefore + threads, pool.IdleThreadCount);
        Assert.Equal(createdBefore + threads, pool.CreatedThreadCount);
    }

    /// <summary>How the rent of a launch fails after it has started new threads.</summary>
    public enum RentFailure
    {
        /// <summary>Its wait for the gate that counts the new threads is interrupted.</summary>
        InterruptedWhileCounting,

        /// <summary>
        /// It fails with an interruption, and its wait for the gate that returns the threads is interrupted.
        /// </summary>
        InterruptionThenInterruptedWhileReturning,

        /// <summary>
        /// It fails with another exception, and its wait for the gate that returns the threads is interrupted.
        /// </summary>
        FailureThenInterruptedWhileReturning
    }

    // Runs a launch of <threads> threads, more than the pool holds, on the calling thread. Once its rent has started
    // the new threads, the rent asks for the gate, waits until another thread holds it, sets <started> and fails as
    // <failure> says.
    // [WHY] The hook, not a gate released and taken again, decides where the rent waits: a released Monitor is not
    // handed to the thread that waited for it, so the thread that took it back could get it first, and the rent's
    // next wait would still be the one that takes threads from the pool
    private static RentOutcome RunWithFailingRent(int threads, RentFailure failure, ManualResetEventSlim holdRequested,
        ManualResetEventSlim gateHeld, ManualResetEventSlim started)
    {
        var kernelRan = false;
        var pool = ManagedStackThreadPool.Instance;
        pool.ThreadsStartedForTest = () =>
        {
            holdRequested.Set();
            Assert.True(gateHeld.Wait(s_blockTimeout), "The holder did not take the gate");
            started.Set();
            FailRent(failure);
        };
        try
        {
            var thrown = Record.Exception(() => ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0,
                "Test.RentFails", _ => kernelRan = true));
            return new RentOutcome(thrown, InterruptionPending(), kernelRan);
        }
        finally
        {
            pool.ThreadsStartedForTest = null;
        }
    }

    private static void FailRent(RentFailure failure)
    {
        switch (failure)
        {
            case RentFailure.InterruptionThenInterruptedWhileReturning:
                throw new ThreadInterruptedException(RentFailureMessage);
            case RentFailure.FailureThenInterruptedWhileReturning:
                throw new InvalidOperationException(RentFailureMessage);
        }
    }

    // Interrupts <scheduler>, which waits for the gate that the calling thread holds, and returns once the interruption
    // has been thrown on it.
    // [WHY] The gate goes when this returns. A blocked state does not prove that the wait for the gate has begun: an
    // interruption that comes before it stays pending, and a gate released at once could then be taken without a wait,
    // leaving the interruption for a later wait. While the gate is held, the scheduler's next wait that an interruption
    // can end is the wait for the gate, so the interruption is thrown there before the gate goes
    private static void InterruptAndWaitUntilThrown(Thread? scheduler)
    {
        Assert.NotNull(scheduler);
        using var watch = new InterruptionWatch(scheduler);
        scheduler.Interrupt();
        Assert.True(SpinWait.SpinUntil(() => watch.Thrown, s_blockTimeout),
            "The interruption was not thrown on the scheduler");
    }

    // Runs a launch of <threads> threads with <context> on the calling thread. Its last GPU thread asks for the gate
    // and waits until another thread holds it, so that the scheduler finds the gate held when it returns the threads.
    // With <armContext>, that GPU thread first arms the context, while the scheduler waits in it for this GPU thread:
    // that wait ends as usual, and every later wait of the context fails
    private static void RunHoldingTheGateInTheLastThread(int threads, CountingWaitContext context,
        ManualResetEventSlim holdRequested, ManualResetEventSlim gateHeld, bool armContext)
    {
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0, "Test.HoldGateInTheLastThread", _ =>
            {
                // The threads run in index order and none waits at a barrier: this is the last step
                if (ManagedGpuContext.ThreadIdxX != threads - 1)
                {
                    return;
                }

                if (armContext)
                {
                    Assert.True(SpinWait.SpinUntil(() => context.InWait, s_blockTimeout),
                        "The scheduler's wait did not enter the synchronization context");
                    context.Arm();
                }

                holdRequested.Set();
                Assert.True(gateHeld.Wait(s_blockTimeout), "The holder did not take the gate");
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    private static bool IsBlocked(Thread? thread) =>
        thread is not null && (thread.ThreadState & ThreadState.WaitSleepJoin) != 0;

    // Whether <scheduler> is blocked in a wait that does not go through <context>. While a GPU thread runs, the
    // scheduler's only wait is the one for that thread, through the context; after the last answer, its next wait is
    // the one for the pool's gate, which the pool takes with the context cleared.
    // [WHY] A blocked state alone may still be the wait for the last answer, and an interruption would end that wait
    // instead. The counts are read around the state: no wait of the context in progress before it, and none entered
    // after it, so the blocked state belongs to a wait outside the context
    private static bool BlockedOutsideTheContext(Thread? scheduler, CountingWaitContext context)
    {
        int entered = context.EnteredWaits;
        if (context.LeftWaits != entered)
        {
            return false;
        }

        return IsBlocked(scheduler) && context.EnteredWaits == entered;
    }

    // Whether the calling thread had an interruption pending: Sleep(0) throws it
    private static bool InterruptionPending()
    {
        try
        {
            Thread.Sleep(0);
            return false;
        }
        catch (ThreadInterruptedException)
        {
            return true;
        }
    }

    // What a launch whose rent failed gave its launching thread
    private sealed record RentOutcome(Exception? Failure, bool InterruptionPending, bool KernelRan);

    // Records that the runtime threw a ThreadInterruptedException on <thread> (first chance), other than the one that
    // FailRent throws itself
    private sealed class InterruptionWatch : IDisposable
    {
        private readonly Thread _thread;
        private int _thrown;

        public InterruptionWatch(Thread thread)
        {
            _thread = thread;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        }

        public bool Thrown => Volatile.Read(ref _thrown) != 0;

        public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;

        private void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is ThreadInterruptedException { Message: not RentFailureMessage } &&
                Thread.CurrentThread == _thread)
            {
                Volatile.Write(ref _thrown, 1);
            }
        }
    }

    // A thread that takes the pool's gate once <requested> is set, sets <held>, runs <whileHeld> and lets the gate go
    private sealed class GateHolder
    {
        private readonly Thread _thread;
        private Exception? _failure;

        public GateHolder(ManualResetEventSlim requested, ManualResetEventSlim held, Action whileHeld)
        {
            _thread = new Thread(() => Hold(requested, held, whileHeld))
            {
                // A holder that hangs fails the test through the join timeout and must not keep the process alive
                IsBackground = true
            };
            _thread.Start();
        }

        public void AssertEnded()
        {
            Assert.True(_thread.Join(s_joinTimeout), "The thread that held the gate did not end");
            Assert.Null(_failure);
        }

        private void Hold(ManualResetEventSlim requested, ManualResetEventSlim held, Action whileHeld)
        {
            try
            {
                Assert.True(requested.Wait(s_blockTimeout), "No thread asked for the gate");
                using (ManagedStackThreadPool.Instance.HoldGateForTest())
                {
                    held.Set();
                    whileHeld();
                }
            }
            catch (Exception ex)
            {
                _failure = ex;
            }
        }
    }
}
