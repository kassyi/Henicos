using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Validates <see cref="CudaStreamCompletion"/> against a watchdog tick that is held inside the CUDA driver, as when
/// <c>cuMemFree</c> or <c>cuMemFreeHost</c> on another thread holds a lock of the driver until every stream drains.
/// </summary>
/// <remarks>
/// A regression deadlocks the freeing thread, the watchdog and the driver's host-function thread for good, and every
/// later context switch of the process behind them. The tests that free memory or cap the thread pool therefore run in
/// a child process of their own (<see cref="IsolatedTestProcess"/>); the others release the tick barrier in a
/// <c>finally</c>, which ends a regression.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class CudaStreamCompletionDriverLockTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    // Several watchdog periods (100 ms), long enough for a deferred signal to reach the awaiter.
    private static readonly TimeSpan WatchdogQuietPeriod = TimeSpan.FromMilliseconds(400);

    // A deadlocked child fails its own wait after CompletionTimeout; this bounds a child that cannot even exit.
    private const int IsolatedRunTimeoutMilliseconds = 60_000;

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void HostCallback_TickInsideDriver_StreamDrainsAndWaitCompletesAfterTheTick()
    {
        CudaCapability.EnsureGpuAvailable();

        using var isolated = IsolatedContext.Create();
        var stream = isolated.Run(static () => new CudaStream());
        try
        {
            using var gate = isolated.Run(() => StreamGate.Enqueue(stream.Handle, TimeSpan.FromMilliseconds(300)));

            // Kept as a ValueTask: its IsCompleted reads the source itself, while a Task from AsTask completes only
            // when a thread-pool continuation has run, which may lag behind the signal.
            var pending = CudaStreamCompletion.WaitAsync(stream.Handle);
            var tickLock = CudaStreamCompletionSource.FindTickLock(isolated.Handle)
                           ?? throw new InvalidOperationException("The wait was not registered as armed.");

            // Holding the tick barrier simulates a watchdog tick that is inside the driver, as when cuMemFree on
            // another thread keeps the tick's cuCtxSetCurrent waiting for this very stream.
            tickLock.Enter();
            var synchronize = OnOwnThread(() => isolated.Run(() => CudaDriverApi.cuStreamSynchronize(stream.Handle)));
            try
            {
                // The stream drains only once the host callback has returned, so the callback must not wait for the
                // tick.
                // mutant: CudaStreamCompletionSource.CompleteFromHostCallback
                // `if (_tickLock.TryEnter())` -> `if (_tickLock.TryEnter(Timeout.Infinite))`
                Assert.True(
                    synchronize.Wait(CompletionTimeout),
                    "The host callback waited for the tick barrier and kept the stream from draining.");
                Assert.Equal(CUresult.CudaSuccess, synchronize.Result);
                Assert.True(gate.IsReleased);

                // The awaiter may resume (and destroy the context) only after the tick has left the driver. Observed
                // for several watchdog periods, since a deferred signal runs on the thread pool.
                // mutant: CudaStreamCompletionSource.CompleteFromHostCallback
                // `lock (s_armedLock) { s_armed.Remove(this); } Signal(error); return;` added before the work item
                // mutant: CudaStreamCompletionSource.CompleteFromHostCallback, the work item
                // `state.Source.PassBarrierAndSignal(state.Error)` -> removes the armed entry and calls
                // `state.Source.Signal(state.Error)` without passing the barrier
                Assert.False(
                    SpinWait.SpinUntil(() => pending.IsCompleted, WatchdogQuietPeriod),
                    "The wait completed while a tick was still inside the driver.");
            }
            finally
            {
                tickLock.Exit();

                // On a regression the synchronization returns only now; the context must not be destroyed under it.
                synchronize.Wait(CompletionTimeout);
            }

            Assert.True(
                pending.AsTask().Wait(CompletionTimeout),
                "The wait did not complete after the tick left the driver.");
        }
        finally
        {
            isolated.Run(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void WatchdogTick_BarrierHeldLongerThanThePoolHasThreads_PoolStaysResponsive() =>
        RunInChildProcess(
            nameof(WatchdogTick_BarrierHeldLongerThanThePoolHasThreads_PoolStaysResponsive),
            HoldTickBarrierLongerThanThePoolHasThreads);

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void WaitAsync_DeviceMemoryFreedWhileStreamIsPendingForSeveralWatchdogPeriods_Completes() =>
        RunInChildProcess(
            nameof(WaitAsync_DeviceMemoryFreedWhileStreamIsPendingForSeveralWatchdogPeriods_Completes),
            static () => FreeDeviceMemoryWhileWaitIsPending(GateHoldTime));

    // Control: the gate opens before the first watchdog tick (100 ms), so no tick is inside the driver.
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void WaitAsync_DeviceMemoryFreedWhileStreamIsPendingForLessThanAWatchdogPeriod_Completes() =>
        RunInChildProcess(
            nameof(WaitAsync_DeviceMemoryFreedWhileStreamIsPendingForLessThanAWatchdogPeriod_Completes),
            static () => FreeDeviceMemoryWhileWaitIsPending(TimeSpan.FromMilliseconds(30)));

    private static void HoldTickBarrierLongerThanThePoolHasThreads()
    {
        // The limits of the pool are process-wide, hence the child process. A tick that waited for the barrier
        // would take one pool thread per watchdog period (100 ms) until none is left.
        var cap = Environment.ProcessorCount;
        ThreadPool.GetMinThreads(out _, out var minIo);
        ThreadPool.GetMaxThreads(out _, out var maxIo);
        Assert.True(ThreadPool.SetMinThreads(cap, minIo));
        Assert.True(ThreadPool.SetMaxThreads(cap, maxIo));

        using var isolated = IsolatedContext.Create();
        var stream = isolated.Run(static () => new CudaStream());
        var gate = isolated.Run(() => StreamGate.Enqueue(stream.Handle, TimeSpan.FromMinutes(1)));
        var pending = CudaStreamCompletion.WaitAsync(stream.Handle);
        var tickLock = CudaStreamCompletionSource.FindTickLock(isolated.Handle)
                       ?? throw new InvalidOperationException("The wait was not registered as armed.");

        using var canary = new ManualResetEventSlim(false);
        bool canaryRan;
        tickLock.Enter();
        try
        {
            // More watchdog periods than the pool has threads, then a work item that needs one of them.
            Thread.Sleep((cap + 6) * 100);
            ThreadPool.UnsafeQueueUserWorkItem(static e => e.Set(), canary, preferLocal: false);
            canaryRan = canary.Wait(TimeSpan.FromSeconds(2));
        }
        finally
        {
            tickLock.Exit();
        }

        // Opens the gate early.
        gate.Dispose();
        Assert.True(pending.AsTask().Wait(CompletionTimeout), "The wait did not complete after the gate opened.");
        isolated.Run(stream.Dispose);

        // mutant: CudaStreamCompletionSource.OnWatchdogTick
        // `if (!_tickLock.TryEnter())` -> `if (!_tickLock.TryEnter(Timeout.Infinite))`
        Assert.True(canaryRan, "Ticks piled up behind the barrier and took every thread of the pool.");
    }

    private static void FreeDeviceMemoryWhileWaitIsPending(TimeSpan gateHoldTime)
    {
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        var buffer = CudaTestEnvironment.InContext(static () =>
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var pointer, 4096));
            return pointer;
        });
        var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(stream.Handle, gateHoldTime));

        var pending = CudaStreamCompletion.WaitAsync(stream.Handle).AsTask();

        // cuMemFree waits for the work of every stream of the device, so it returns only after the gate opens.
        var free = OnOwnThread(() => CudaTestEnvironment.InContext(() => CudaDriverApi.cuMemFree_v2(buffer)));

        // A blocking wait on the test thread: stuck watchdog ticks take thread-pool threads, so a continuation or a
        // Task.Delay would itself run late. After a failure nothing is freed: the process is stuck and ends here.
        // mutant: CudaStreamCompletionSource.CompleteFromHostCallback
        // `if (_tickLock.TryEnter())` -> `if (_tickLock.TryEnter(Timeout.Infinite))`
        var completed = Task.WaitAll([pending, free], CompletionTimeout);
        Assert.True(
            completed,
            $"Deadlock: gate released = {gate.IsReleased}, wait completed = {pending.IsCompleted}, " +
            $"cuMemFree returned = {free.IsCompleted}.");

        Assert.Equal(CUresult.CudaSuccess, free.Result);
        Assert.True(gate.IsReleased);
        gate.Dispose();
        CudaTestEnvironment.InContext(stream.Dispose);
    }

    // Runs the test body in a child process started for this one test, and the body itself in that child.
    private static void RunInChildProcess(string testMethodName, Action body)
    {
        CudaCapability.EnsureGpuAvailable();
        if (!IsolatedTestProcess.IsChild(testMethodName))
        {
            IsolatedTestProcess.Run(typeof(CudaStreamCompletionDriverLockTests), testMethodName,
                IsolatedRunTimeoutMilliseconds);
            return;
        }

        body();
    }

    // A driver call that may block, on a thread of its own rather than one of the pool.
    private static Task<CUresult> OnOwnThread(Func<CUresult> driverCall) =>
        Task.Factory.StartNew(driverCall, CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
}
