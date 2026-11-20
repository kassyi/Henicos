#nullable enable
using System;
using System.Threading;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

/// <summary>
/// The process-wide pool of stack threads that <see cref="ManagedStackRunner"/> runs GPU threads on: reuse, and the
/// launches whose waits are interrupted, fail or run another launch, after which the pool must still be whole.
/// </summary>
/// <remarks>
/// The pool is process-wide, so these tests run with no other test in parallel: a launch of another test would
/// otherwise move the counters these tests read, or take the pooled threads that a test means to reuse.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class ManagedStackPoolTests
{
    private static readonly TimeSpan s_joinTimeout = TimeSpan.FromSeconds(60);

    // Shorter than the join of RunOnNewThread, so that a scheduler that never blocks is reported by its own message
    private static readonly TimeSpan s_blockTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_ManyLaunches_ReusesTheThreads()
    {
        ManagedStackRunner.Run(1, 1, 1, 64, 1, 1, 0, "Test.WarmupPool", _ =>
        {
            ManagedGpuContext.__gsharp_stack_block_barrier();
        });

        int createdAfterFirst = ManagedStackRunner.CreatedThreadCount;
        int idleAfterFirst = ManagedStackRunner.IdleThreadCount;

        for (int i = 0; i < 199; i++)
        {
            ManagedStackRunner.Run(1, 1, 1, 64, 1, 1, 0, "Test.ReusePool", _ =>
            {
                ManagedGpuContext.__gsharp_stack_block_barrier();
            });

            Assert.Equal(createdAfterFirst, ManagedStackRunner.CreatedThreadCount);
            Assert.Equal(idleAfterFirst, ManagedStackRunner.IdleThreadCount);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_LaunchAfterFailedLaunch_ReusesTheThreads()
    {
        RunBothFailedShapes();
        int createdAfterWarmup = ManagedStackRunner.CreatedThreadCount;

        for (int i = 0; i < 50; i++)
        {
            RunBothFailedShapes();
            Assert.Equal(createdAfterWarmup, ManagedStackRunner.CreatedThreadCount);
        }

        static void RunBothFailedShapes()
        {
            using (ManagedStackRunner.UseThreadOrder(round =>
                       round.Round == 1 ? [0, 1, 2, 3] : [0, 0, 1, 2]))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.CancelReuse", _ =>
                    {
                        ManagedGpuContext.__gsharp_stack_block_barrier();
                    }));

                Assert.Throws<ArgumentException>(() =>
                    ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0, "Test.FailAndCancelReuse", _ =>
                    {
                        if (ManagedGpuContext.ThreadIdxX == 0)
                        {
                            throw new ArgumentException("thread failure");
                        }

                        ManagedGpuContext.__gsharp_stack_block_barrier();
                    }));
            }
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_SchedulerInterruptedWhileAThreadRuns_ThrowsAndLaterLaunchesGiveTheirResults()
    {
        const int threads = 8;
        Thread? scheduler = null;
        var interruptedOutput = new int[threads];

        var results = RunOnNewThread(() =>
        {
            scheduler = Thread.CurrentThread;
            Assert.Throws<ThreadInterruptedException>(() =>
                ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.InterruptScheduler", _ =>
                {
                    int local = ManagedGpuContext.ThreadIdxX;
                    if (local == 0)
                    {
                        // The interruption ends the scheduler's wait for this thread
                        InterruptOnceBlocked(scheduler);
                    }

                    ManagedGpuContext.__gsharp_stack_block_barrier();
                    interruptedOutput[local] = 1;
                }));

            return RunLaterLaunches(threads, 100);
        });

        Assert.Equal(new int[threads], interruptedOutput);
        AssertLaterLaunches(results, threads, 100);

        // The pooled threads that ran the interrupted launch run a launch of another thread too
        Assert.Equal(ExpectedReverse(64, 7), RunReverse(64, 7));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_SchedulerInterruptedWhileUnwinding_UnwindsEveryThreadAndReturnsItToThePool()
    {
        const int threads = 4;
        Thread? scheduler = null;
        var resumedAfterBarrier = 0;
        var unwound = 0;

        // The pool holds a block of idle threads, so that neither launch below starts a new one unless the first
        // launch leaves some of its threads behind
        Assert.Equal(ExpectedReverse(threads, 1), RunReverse(threads, 1));
        int createdBefore = ManagedStackRunner.CreatedThreadCount;

        var after = RunOnNewThread(() =>
        {
            scheduler = Thread.CurrentThread;
            using (ManagedStackRunner.UseThreadOrder(round =>
                       round.Round == 1 ? [0, 1, 2, 3] : throw new InvalidOperationException("second round")))
            {
                // Round 2 fails, the launch cancels the threads at the barrier, and thread 0 interrupts the scheduler
                // while it unwinds: the unwinding still reaches threads 1 to 3
                Assert.Throws<ThreadInterruptedException>(() =>
                    ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.InterruptUnwind", _ =>
                    {
                        int local = ManagedGpuContext.ThreadIdxX;
                        try
                        {
                            ManagedGpuContext.__gsharp_stack_block_barrier();
                            Interlocked.Increment(ref resumedAfterBarrier);
                        }
                        finally
                        {
                            Interlocked.Increment(ref unwound);
                            if (local == 0)
                            {
                                InterruptOnceBlocked(scheduler);
                            }
                        }
                    }));
            }

            return RunReverse(threads, 30);
        });

        Assert.Equal(ExpectedReverse(threads, 30), after);
        Assert.Equal(threads, Volatile.Read(ref unwound));
        Assert.Equal(0, Volatile.Read(ref resumedAfterBarrier));
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_SchedulerInterruptedWhileWaitingForThePoolGate_ThrowsAndReturnsEveryThreadToThePool()
    {
        const int threads = 4;
        Thread? scheduler = null;
        Exception? holderFailure = null;
        var context = new GateWaitContext();
        using var holdRequested = new ManualResetEventSlim();
        using var gateHeld = new ManualResetEventSlim();

        // The pool holds a block of idle threads, so that the launch below takes them instead of starting new ones
        Assert.Equal(ExpectedReverse(threads, 1), RunReverse(threads, 1));
        int createdBefore = ManagedStackRunner.CreatedThreadCount;
        int idleBefore = ManagedStackRunner.IdleThreadCount;
        int idleAfterInterrupt = -1;

        // Holds the gate of the pool from inside the last GPU thread of the launch until the scheduler, which has
        // taken the last answer, blocks in Return's wait for the gate; interrupts it there, then lets the gate go
        var holder = new Thread(() =>
        {
            try
            {
                Assert.True(holdRequested.Wait(s_blockTimeout), "The last GPU thread did not ask for the gate");
                using (ManagedStackThreadPool.Instance.HoldGateForTest())
                {
                    gateHeld.Set();
                    InterruptOnceBlockedAtTheGate(scheduler, context);
                }
            }
            catch (Exception ex)
            {
                holderFailure = ex;
            }
        })
        {
            IsBackground = true
        };
        holder.Start();

        var after = RunOnNewThread(() =>
        {
            scheduler = Thread.CurrentThread;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Assert.Throws<ThreadInterruptedException>(() =>
                    ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.InterruptGateWait", _ =>
                    {
                        int local = ManagedGpuContext.ThreadIdxX;
                        if (local == 0)
                        {
                            Assert.True(SpinWait.SpinUntil(context.TryMarkSchedulerWait, s_blockTimeout),
                                "The scheduler's wait did not enter the synchronization context");
                        }

                        // The threads run in index order and none waits at a barrier: this is the last step
                        if (local == threads - 1)
                        {
                            holdRequested.Set();
                            Assert.True(gateHeld.Wait(s_blockTimeout), "The holder did not take the gate");
                        }
                    }));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }

            idleAfterInterrupt = ManagedStackRunner.IdleThreadCount;
            return RunReverse(threads, 30);
        });

        Assert.True(holder.Join(s_joinTimeout));
        Assert.Null(holderFailure);
        Assert.Equal(idleBefore, idleAfterInterrupt);
        Assert.Equal(ExpectedReverse(threads, 30), after);
        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(WaitFailureMode.InnerLaunchBeforeTheWait, false)]
    [InlineData(WaitFailureMode.InnerLaunchAfterTheWait, false)]
    [InlineData(WaitFailureMode.InnerLaunchAfterTheWait, true)]
    [InlineData(WaitFailureMode.EveryWait, false)]
    [InlineData(WaitFailureMode.EveryWait, true)]
    public void Run_WaitOfTheSchedulerThrows_ThrowsAndLaterLaunchesOfTheThreadGiveTheirResults(WaitFailureMode mode,
        bool launchesFirst)
    {
        const int threads = 8;
        Exception? caught = null;
        bool everyWait = mode == WaitFailureMode.EveryWait;
        int[][] before = [];

        var results = RunOnNewThread(() =>
        {
            // Launches of the thread before the failure, so that the failure is not in the thread's first launch
            if (launchesFirst)
            {
                before = RunLaterLaunches(threads, 1000);
            }

            // The first wait runs a launch whose kernel fails, before or after it has taken the signal it waited for,
            // and that failure leaves the Wait of the context; or every wait fails
            var context = new ThrowingContext(mode, () => ManagedStackRunner.Run(1, 1, 1, 4, 1, 1, 0,
                "Test.InnerFails", _ =>
                {
                    ManagedGpuContext.__gsharp_stack_block_barrier();
                    throw new ArgumentException("inner failure");
                }));
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                caught = Record.Exception(() => RunReverse(threads, 40, () => context.Entered));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }

            return RunLaterLaunches(threads, 100);
        });

        if (everyWait)
        {
            Assert.Equal(ThrowingContext.WaitFailure, Assert.IsType<InvalidOperationException>(caught).Message);
        }
        else
        {
            Assert.Equal("inner failure", Assert.IsType<ArgumentException>(caught).Message);
        }

        AssertLaterLaunches(before, threads, 1000);
        AssertLaterLaunches(results, threads, 100);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_SchedulerInterruptedTwiceInTheLastWait_FailsOnceAndLaterLaunchesGiveTheirResults()
    {
        const int threads = 4;
        Thread? scheduler = null;

        var results = RunOnNewThread(() =>
        {
            scheduler = Thread.CurrentThread;
            var context = new FailedWaitContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                // The only GPU thread interrupts the scheduler, which waits for it, then again while the scheduler
                // still waits for its answer; the launch has no wait after that one
                Assert.Throws<ThreadInterruptedException>(() =>
                    ManagedStackRunner.Run(1, 1, 1, 1, 1, 1, 0, "Test.InterruptTwice", _ =>
                    {
                        InterruptOnceBlocked(scheduler);
                        // The first interruption has ended the scheduler's wait; its next wait is the one for the answer
                        Assert.True(SpinWait.SpinUntil(() => context.FailedWaits > 0, s_blockTimeout),
                            "The first interruption did not end the scheduler's wait");
                        InterruptOnceBlocked(scheduler);
                    }));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }

            // The second interruption came while the scheduler still waited for the answer: it was dropped
            Assert.Equal(1, ManagedStackRunner.DiscardedInterruptsOnThisThread);
            return RunLaterLaunches(threads, 10);
        });

        AssertLaterLaunches(results, threads, 10);
    }

    /// <summary>How the <see cref="SynchronizationContext.Wait"/> of the scheduler fails.</summary>
    public enum WaitFailureMode
    {
        /// <summary>The first wait runs a failing launch before it waits.</summary>
        InnerLaunchBeforeTheWait,

        /// <summary>The first wait takes the signal and then runs a failing launch.</summary>
        InnerLaunchAfterTheWait,

        /// <summary>Every wait throws without waiting.</summary>
        EveryWait
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_KernelInterruptsItsOwnThread_FailsTheLaunchAndLaterLaunchesGiveTheirResults(bool beforeBarrier)
    {
        const int threads = 8;

        // Thread 3 interrupts itself before the barrier (the barrier's wait takes it) or after it, at the end of the
        // kernel, where no wait of the kernel takes it
        Assert.Throws<ThreadInterruptedException>(() =>
            ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.SelfInterrupt", _ =>
            {
                bool interrupts = ManagedGpuContext.ThreadIdxX == 3;
                if (interrupts && beforeBarrier)
                {
                    Thread.CurrentThread.Interrupt();
                }

                ManagedGpuContext.__gsharp_stack_block_barrier();
                if (interrupts && !beforeBarrier)
                {
                    Thread.CurrentThread.Interrupt();
                }
            }));

        AssertLaterLaunches(RunLaterLaunches(threads, 10), threads, 10);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_IdlePooledThreadsInterrupted_AbsorbTheInterruptionAndRunLaterLaunches()
    {
        const int threads = 8;
        var workers = new Thread?[threads];
        ManagedStackRunner.Run(1, 1, 1, threads, 1, 1, 0, "Test.CaptureWorkers", _ =>
        {
            workers[ManagedGpuContext.ThreadIdxX] = Thread.CurrentThread;
        });
        int absorbedBefore = ManagedStackRunner.AbsorbedInterruptCount;
        int createdBefore = ManagedStackRunner.CreatedThreadCount;

        foreach (var worker in workers)
        {
            Assert.NotNull(worker);
            worker.Interrupt();
        }

        Assert.True(SpinWait.SpinUntil(
            () => ManagedStackRunner.AbsorbedInterruptCount - absorbedBefore >= threads, s_joinTimeout));
        AssertLaterLaunches(RunLaterLaunches(threads, 10), threads, 10);

        Assert.Equal(createdBefore, ManagedStackRunner.CreatedThreadCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Run_WaitOfTheSchedulerRunsAnotherLaunch_BothLaunchesGiveTheirResults()
    {
        const int threads = 8;
        int[]? inner = null;
        var context = new ReentrantContext(() => inner = RunReverse(threads, 500));

        object? stateBefore = null;
        object? stateAfter = null;

        var outer = RunOnNewThread(() =>
        {
            stateBefore = ManagedStackRunner.BlockStateOfThisThread;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                return RunReverse(threads, 20, () => context.Entered);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
                stateAfter = ManagedStackRunner.BlockStateOfThisThread;
            }
        });

        Assert.True(context.RanInner);
        Assert.Equal(ExpectedReverse(threads, 20), outer);
        // The inner launch ran on a block state of its own and gave the thread its own back
        Assert.NotNull(stateBefore);
        Assert.Same(stateBefore, stateAfter);
        Assert.NotNull(inner);
        Assert.Equal(ExpectedReverse(threads, 500), inner);
    }

    // Each thread writes <offset> + its index, waits at a barrier, and reads the value of the mirrored thread.
    // [WHY] With <entered>, thread 0 first waits until it holds: the scheduler cannot take the answer of thread 0
    // before its wait, so that wait must block and enter the synchronization context that <entered> watches
    private static int[] RunReverse(int threads, int offset, Func<bool>? entered = null)
    {
        var scratch = new int[threads];
        var output = new int[threads];
        ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0, "Test.Reverse", _ =>
        {
            int local = ManagedGpuContext.ThreadIdxX;
            if (local == 0 && entered is not null)
            {
                Assert.True(SpinWait.SpinUntil(entered, s_blockTimeout),
                    "The scheduler's wait did not enter the synchronization context");
            }

            scratch[local] = offset + local;
            ManagedGpuContext.__gsharp_stack_block_barrier();
            output[local] = scratch[threads - 1 - local];
        });
        return output;
    }

    // Three launches of the calling thread, at the offsets <step>, 2 * <step> and 3 * <step>
    private static int[][] RunLaterLaunches(int threads, int step)
    {
        var outputs = new int[3][];
        for (int i = 0; i < outputs.Length; i++)
        {
            outputs[i] = RunReverse(threads, step * (i + 1));
        }

        return outputs;
    }

    private static void AssertLaterLaunches(int[][] outputs, int threads, int step)
    {
        for (int i = 0; i < outputs.Length; i++)
        {
            Assert.Equal(ExpectedReverse(threads, step * (i + 1)), outputs[i]);
        }
    }

    private static int[] ExpectedReverse(int threads, int offset)
    {
        var expected = new int[threads];
        for (int t = 0; t < threads; t++)
        {
            expected[t] = offset + threads - 1 - t;
        }

        return expected;
    }

    // Interrupts <scheduler> once it is blocked in its wait for the calling GPU thread.
    // [WHY] An interruption of a thread that is not blocked stays pending until the thread next blocks, and the
    // scheduler does not block for a thread whose answer has already come (ManagedBatonSignal.Wait). Interrupted before
    // it blocks, it would take the interruption in its wait for a later thread, or in none
    private static void InterruptOnceBlocked(Thread? scheduler)
    {
        Assert.NotNull(scheduler);
        Assert.True(
            SpinWait.SpinUntil(() => (scheduler.ThreadState & ThreadState.WaitSleepJoin) != 0, s_blockTimeout),
            "The scheduler did not block in its wait for this GPU thread");
        scheduler.Interrupt();
    }

    // Interrupts <scheduler> once it is blocked in its wait for the pool's gate, a wait of <context> on another handle
    // than the scheduler's waits for its GPU threads.
    // [WHY] A blocked state alone may still be the scheduler's wait for the last answer, and the interruption would end
    // that wait instead. The count of the gate's waits is read first: the wait for the answer has then ended, and a
    // blocked state read after it belongs to the wait for the gate
    private static void InterruptOnceBlockedAtTheGate(Thread? scheduler, GateWaitContext context)
    {
        Assert.NotNull(scheduler);
        Assert.True(SpinWait.SpinUntil(
                () => context.InOtherWait && (scheduler.ThreadState & ThreadState.WaitSleepJoin) != 0,
                s_blockTimeout),
            "The scheduler did not block in its wait for the gate of the pool");
        scheduler.Interrupt();
    }

    private static T RunOnNewThread<T>(Func<T> body) where T : class
    {
        T? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            // A thread that hangs fails the test through the join timeout and must not keep the process alive
            IsBackground = true
        };
        thread.Start();
        Assert.True(thread.Join(s_joinTimeout));
        Assert.Null(failure);
        Assert.NotNull(result);
        return result;
    }

    // Asks for wait notification and records that its Wait was entered
    private abstract class EnteredWaitContext : SynchronizationContext
    {
        private int _entered;

        protected EnteredWaitContext() => SetWaitNotificationRequired();

        public bool Entered => Volatile.Read(ref _entered) != 0;

        protected void MarkEntered() => Volatile.Write(ref _entered, 1);
    }

    // Fails a wait as <mode> says: the first wait runs <inner> (which throws) before or
    // after it waits, or every wait throws WaitFailure
    private sealed class ThrowingContext : EnteredWaitContext
    {
        public const string WaitFailure = "the wait of the context failed";

        private readonly WaitFailureMode _mode;
        private readonly Action _inner;
        private bool _ranInner;

        public ThrowingContext(WaitFailureMode mode, Action inner)
        {
            _mode = mode;
            _inner = inner;
        }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            MarkEntered();
            if (_mode == WaitFailureMode.EveryWait)
            {
                throw new InvalidOperationException(WaitFailure);
            }

            if (_ranInner)
            {
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }

            _ranInner = true;
            if (_mode == WaitFailureMode.InnerLaunchBeforeTheWait)
            {
                _inner();
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }

            int result = WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            _inner();
            return result;
        }
    }

    // Asks for wait notification and counts the waits that an interruption ended
    private sealed class FailedWaitContext : SynchronizationContext
    {
        private int _failedWaits;

        public FailedWaitContext() => SetWaitNotificationRequired();

        public int FailedWaits => Volatile.Read(ref _failedWaits);

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            try
            {
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }
            catch (ThreadInterruptedException)
            {
                Interlocked.Increment(ref _failedWaits);
                throw;
            }
        }
    }

    // Asks for wait notification and tells the scheduler's waits for its GPU threads from its other waits. A contended
    // Monitor wait, such as the wait for the pool's gate, goes through the context as well, so the waits are told apart
    // by their handle: the wait in progress while a GPU thread runs is the scheduler's wait for it
    private sealed class GateWaitContext : SynchronizationContext
    {
        private IntPtr _currentHandle;
        private IntPtr _schedulerHandle;
        private int _inWait;
        private int _otherWaitsEntered;
        private int _otherWaitsLeft;

        public GateWaitContext() => SetWaitNotificationRequired();

        // A wait on another handle than the marked one has started and not ended
        public bool InOtherWait => Volatile.Read(ref _otherWaitsEntered) > Volatile.Read(ref _otherWaitsLeft);

        // For a GPU thread: marks the handle of the scheduler's wait in progress, once there is one
        public bool TryMarkSchedulerWait()
        {
            if (Volatile.Read(ref _inWait) == 0)
            {
                return false;
            }

            Volatile.Write(ref _schedulerHandle, Volatile.Read(ref _currentHandle));
            return true;
        }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            IntPtr handle = waitHandles.Length == 1 ? waitHandles[0] : IntPtr.Zero;
            IntPtr scheduler = Volatile.Read(ref _schedulerHandle);
            bool other = scheduler != IntPtr.Zero && handle != scheduler;
            if (other)
            {
                Interlocked.Increment(ref _otherWaitsEntered);
            }

            Volatile.Write(ref _currentHandle, handle);
            Volatile.Write(ref _inWait, 1);
            try
            {
                return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
            }
            finally
            {
                Volatile.Write(ref _inWait, 0);
                if (other)
                {
                    Interlocked.Increment(ref _otherWaitsLeft);
                }
            }
        }
    }

    // Asks for wait notification, as the WPF dispatcher does, and runs <inner> inside the first wait
    private sealed class ReentrantContext : EnteredWaitContext
    {
        private readonly Action _inner;

        public ReentrantContext(Action inner)
        {
            _inner = inner;
        }

        public bool RanInner { get; private set; }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            MarkEntered();
            if (!RanInner)
            {
                RanInner = true;
                _inner();
            }

            return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
        }
    }
}
