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

            var afterOutputs = new int[3][];
            for (int i = 0; i < afterOutputs.Length; i++)
            {
                afterOutputs[i] = RunReverse(threads, 100 * (i + 1));
            }

            return afterOutputs;
        });

        Assert.Equal(new int[threads], interruptedOutput);
        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(ExpectedReverse(threads, 100 * (i + 1)), results[i]);
        }

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

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(WaitFailureMode.InnerLaunchBeforeTheWait, 0)]
    [InlineData(WaitFailureMode.InnerLaunchAfterTheWait, 0)]
    [InlineData(WaitFailureMode.InnerLaunchAfterTheWait, 3)]
    [InlineData(WaitFailureMode.EveryWait, 0)]
    [InlineData(WaitFailureMode.EveryWait, 3)]
    public void Run_WaitOfTheSchedulerThrows_ThrowsAndLaterLaunchesOfTheThreadGiveTheirResults(WaitFailureMode mode,
        int launchesBefore)
    {
        const int threads = 8;
        Exception? caught = null;
        bool everyWait = mode == WaitFailureMode.EveryWait;
        var before = new int[launchesBefore][];

        var results = RunOnNewThread(() =>
        {
            // Launches of the thread before the failure, so that the failure is not in the thread's first launch
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = RunReverse(threads, 1000 * (i + 1));
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
                caught = Record.Exception(() => RunReverse(threads, 40));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }

            var afterOutputs = new int[3][];
            for (int i = 0; i < afterOutputs.Length; i++)
            {
                afterOutputs[i] = RunReverse(threads, 100 * (i + 1));
            }

            return afterOutputs;
        });

        if (everyWait)
        {
            Assert.Equal(ThrowingContext.WaitFailure, Assert.IsType<InvalidOperationException>(caught).Message);
        }
        else
        {
            Assert.Equal("inner failure", Assert.IsType<ArgumentException>(caught).Message);
        }

        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(ExpectedReverse(threads, 1000 * (i + 1)), before[i]);
        }

        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(ExpectedReverse(threads, 100 * (i + 1)), results[i]);
        }
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
            // The only GPU thread interrupts the scheduler, which waits for it, then again while the scheduler still
            // waits for its answer; the launch has no wait after that one
            Assert.Throws<ThreadInterruptedException>(() =>
                ManagedStackRunner.Run(1, 1, 1, 1, 1, 1, 0, "Test.InterruptTwice", _ =>
                {
                    InterruptOnceBlocked(scheduler);
                    Thread.Sleep(100);
                    scheduler?.Interrupt();
                }));

            // The second interruption came while the scheduler still waited for the answer: it was dropped
            Assert.Equal(1, ManagedStackRunner.DiscardedInterruptsOnThisThread);

            var afterOutputs = new int[3][];
            for (int i = 0; i < afterOutputs.Length; i++)
            {
                afterOutputs[i] = RunReverse(threads, 10 * (i + 1));
            }

            return afterOutputs;
        });

        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(ExpectedReverse(threads, 10 * (i + 1)), results[i]);
        }
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

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(ExpectedReverse(threads, 10 * (i + 1)), RunReverse(threads, 10 * (i + 1)));
        }
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
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(ExpectedReverse(threads, 10 * (i + 1)), RunReverse(threads, 10 * (i + 1)));
        }

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
                return RunReverse(threads, 20);
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

    // Each thread writes <offset> + its index, waits at a barrier, and reads the value of the mirrored thread
    private static int[] RunReverse(int threads, int offset)
    {
        var scratch = new int[threads];
        var output = new int[threads];
        ManagedStackRunner.Run(1, 1, 1, (uint)threads, 1, 1, 0, "Test.Reverse", _ =>
        {
            int local = ManagedGpuContext.ThreadIdxX;
            scratch[local] = offset + local;
            ManagedGpuContext.__gsharp_stack_block_barrier();
            output[local] = scratch[threads - 1 - local];
        });
        return output;
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
        Assert.True(SpinWait.SpinUntil(
            () => (scheduler.ThreadState & ThreadState.WaitSleepJoin) != 0, s_joinTimeout));
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

    // Asks for wait notification and fails a wait as <mode> says: the first wait runs <inner> (which throws) before or
    // after it waits, or every wait throws WaitFailure
    private sealed class ThrowingContext : SynchronizationContext
    {
        public const string WaitFailure = "the wait of the context failed";

        private readonly WaitFailureMode _mode;
        private readonly Action _inner;
        private bool _ranInner;

        public ThrowingContext(WaitFailureMode mode, Action inner)
        {
            _mode = mode;
            _inner = inner;
            SetWaitNotificationRequired();
        }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
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

    // Asks for wait notification, as the WPF dispatcher does, and runs <inner> inside the first wait
    private sealed class ReentrantContext : SynchronizationContext
    {
        private readonly Action _inner;

        public ReentrantContext(Action inner)
        {
            _inner = inner;
            SetWaitNotificationRequired();
        }

        public bool RanInner { get; private set; }

        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            if (!RanInner)
            {
                RanInner = true;
                _inner();
            }

            return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
        }
    }
}
