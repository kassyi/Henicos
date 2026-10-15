using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Validates non-blocking stream completion (<see cref="CudaStreamCompletion"/>) and the
/// <c>SynchronizeAsync</c> APIs built on top of it.
/// </summary>
/// <remarks>
/// <para>
/// A host-function gate is enqueued on the stream to hold it in a deterministic "not completed" state.
/// Busy-loop kernels are avoided because the device compiler may fold the loop away.
/// </para>
/// <para>
/// The class runs in <see cref="TestCollections.DeviceExclusive"/>: while a gate is closed, the driver delays every host
/// function of the context, and tests running in parallel would block inside device-wide synchronizing calls while
/// holding driver locks that <see cref="CudaStreamCompletion.WaitAsync"/> also needs.
/// </para>
/// <para>
/// <see cref="GpuKernelLoader.CudaContextScope"/> binds the context to the calling OS thread, so it is only held in
/// synchronous sections (<see cref="CudaTestEnvironment.InContext{T}"/>) and never across an <c>await</c>.
/// </para>
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class CudaStreamCompletionTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task WaitAsync_GatedStream_CompletesOnlyAfterGateRelease()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(stream.Handle, GateHoldTime));

            var pending = CudaStreamCompletion.WaitAsync(stream.Handle).AsTask();
            Assert.False(pending.IsCompleted, "Completion must not be signaled while the stream is still gated.");

            await pending.WaitAsync(CompletionTimeout);
            Assert.True(gate.IsReleased, "Completion was signaled before the gated work finished.");
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task WaitAsync_StreamOfAnotherContext_WaitsForThatStream_AndRestoresCurrentContext()
    {
        CudaCapability.EnsureGpuAvailable();

        using var isolated = IsolatedContext.Create();
        var stream = isolated.Run(static () => new CudaStream());
        try
        {
            using var gate = isolated.Run(() => StreamGate.Enqueue(stream.Handle, GateHoldTime));

            // Armed on a thread whose current context is the global one rather than the stream's owner, as when an
            // isolated GpuWorkspace is awaited from ordinary code.
            var (pending, currentAfterArming) = CudaTestEnvironment.InContext(() =>
            {
                var wait = CudaStreamCompletion.WaitAsync(stream.Handle).AsTask();
                CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out var current));
                return (wait, current);
            });

            Assert.Equal(GpuKernelLoader.GlobalContext, currentAfterArming);
            Assert.False(pending.IsCompleted, "Completion must not be signaled while the stream is still gated.");

            await pending.WaitAsync(CompletionTimeout);
            Assert.True(gate.IsReleased, "Completion was signaled before the gated work finished.");
        }
        finally
        {
            isolated.Run(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CudaStream_SynchronizeAsync_CompletesAfterGateRelease()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(stream.Handle, GateHoldTime));

            var pending = stream.SynchronizeAsync().AsTask();
            Assert.False(pending.IsCompleted);

            await pending.WaitAsync(CompletionTimeout);
            Assert.True(gate.IsReleased);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CudaEventTimer_SynchronizeAsync_ElapsedTimeIsValid()
    {
        // Uses the default stream so that the test also runs (with Stopwatch timing) on hosts without a CUDA device.
        using var timer = CudaTestEnvironment.InContext(static () =>
        {
            var created = new CudaEventTimer();
            created.Start();
            Thread.Sleep(20);
            created.Stop();
            return created;
        });

        await timer.SynchronizeAsync().AsTask().WaitAsync(CompletionTimeout);

        // Reading the elapsed time throws CudaErrorNotReady if the end event has not completed yet.
        var elapsed = CudaTestEnvironment.InContext(timer.GetElapsedTimeMilliseconds);
        Assert.True(elapsed >= 0f);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task WaitAsync_ManyConcurrentAwaits_OnIndependentStreams_AllComplete()
    {
        CudaCapability.EnsureGpuAvailable();

        // Exceeds the completion-source pool size so that rent, overflow allocation and eviction are all exercised.
        const int StreamCount = 24;

        var streams = new CudaStream[StreamCount];
        var waits = new Task[StreamCount];
        try
        {
            CudaTestEnvironment.InContext(() =>
            {
                for (var i = 0; i < StreamCount; i++)
                {
                    streams[i] = new CudaStream();
                }
            });

            for (var i = 0; i < StreamCount; i++)
            {
                waits[i] = CudaStreamCompletion.WaitAsync(streams[i].Handle).AsTask();
            }

            // A gate is intentionally not used here: the driver runs host functions of a context on a single thread,
            // so a blocking gate on one stream would also delay completion signals of every other stream.
            await Task.WhenAll(waits).WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(() =>
            {
                foreach (var stream in streams)
                {
                    stream?.Dispose();
                }
            });
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void WaitAsync_RepeatedSequentialAwaits_DoNotAllocateInSteadyState()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();

        // Pre-populates the pool with several sources. With a single pooled source, a release still in progress on the
        // driver's callback thread would force a fresh allocation on this thread whenever that thread is descheduled.
        var warmup = new ValueTask[4];
        for (var round = 0; round < 16; round++)
        {
            for (var i = 0; i < warmup.Length; i++)
            {
                warmup[i] = CudaStreamCompletion.WaitAsync(stream.Handle);
            }

            foreach (var pending in warmup)
            {
                SpinUntilConsumed(pending);
            }
        }

        // A per-call allocation shows up in every window. One-time runtime work on this thread in a cold test process
        // (seen once as a single 280-byte allocation, which is not a fresh completion source) can land in one window,
        // so the steady state is confirmed by the first clean window.
        var allocatedPerWindow = new long[3];
        for (var window = 0; window < allocatedPerWindow.Length; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 256; i++)
            {
                SpinUntilConsumed(CudaStreamCompletion.WaitAsync(stream.Handle));
            }

            allocatedPerWindow[window] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocatedPerWindow[window] == 0)
            {
                return;
            }
        }

        Assert.Fail($"Every window of 256 awaits allocated (bytes per window): {string.Join(", ", allocatedPerWindow)}.");
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void WaitAsync_InCpuFallbackMode_CompletesSynchronously()
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            Assert.True(CudaStreamCompletion.WaitAsync(IntPtr.Zero).IsCompletedSuccessfully);

            using var timer = new CudaEventTimer();
            timer.Start();
            timer.Stop();
            Assert.True(timer.SynchronizeAsync().IsCompletedSuccessfully);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public async Task WaitAsync_PreCanceledToken_ThrowsWithoutWaiting()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // A pre-canceled token returns before any driver call, so no device or context is required.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CudaStreamCompletion.WaitAsync(IntPtr.Zero, cts.Token));
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public async Task WaitAsync_CanceledInFlight_ThrowsAndStreamRemainsUsable()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(stream.Handle, GateHoldTime));
            using var cts = new CancellationTokenSource();

            var pending = CudaStreamCompletion.WaitAsync(stream.Handle, cts.Token).AsTask();

            // Cancel() runs the registration callback before it returns, so the awaiter is detached while the gate is
            // still closed. CancelAsync() would queue the callback and let the gate's success path race it.
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(CompletionTimeout));
            Assert.False(gate.IsReleased, "Cancellation must detach the awaiter before the gated work finishes.");

            // Cancellation only detaches the awaiter; the stream itself must stay valid.
            await stream.SynchronizeAsync().AsTask().WaitAsync(CompletionTimeout);
            Assert.True(gate.IsReleased);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void WaitAsync_OnCapturingStream_ThrowsInvalidOperation()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();

        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream.Handle, 1));
        try
        {
            Assert.Throws<InvalidOperationException>(() => CudaStreamCompletion.WaitAsync(stream.Handle));
        }
        finally
        {
            CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream.Handle, out var graph));
            if (graph != IntPtr.Zero)
            {
                CudaDriverApi.cuGraphDestroy(graph);
            }
        }
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public async Task CudaEventTimer_SynchronizeAsync_AfterDispose_Throws()
    {
        var timer = CudaTestEnvironment.InContext(static () => new CudaEventTimer());
        timer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await timer.SynchronizeAsync());
    }

    private static void SpinUntilConsumed(ValueTask pending)
    {
        // Polls on the calling thread so that no async state machine or continuation is allocated by the test itself.
        // Sleep(1) is disabled: it rounds every await up to a timer tick and stretches a window of 256 awaits to seconds.
        var spinner = new SpinWait();
        while (!pending.IsCompleted)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
        }

        pending.GetAwaiter().GetResult();
    }

    /// <summary>A CUDA context other than the global one, like the context of an isolated <c>GpuWorkspace</c>.</summary>
    private sealed class IsolatedContext : IDisposable
    {
        private IntPtr _handle;

        private IsolatedContext(IntPtr handle) => _handle = handle;

        public static IsolatedContext Create() =>
            CudaTestEnvironment.InContext(static () =>
            {
                CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
                CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out var handle, 0, device));

                // cuCtxCreate also makes the new context current; pop it so that it is current only inside Run.
                CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _));
                return new IsolatedContext(handle);
            });

        public T Run<T>(Func<T> action)
        {
            using var scope = new GpuKernelLoader.CudaContextScope(_handle);
            return action();
        }

        public void Run(Action action)
        {
            using var scope = new GpuKernelLoader.CudaContextScope(_handle);
            action();
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            var handle = _handle;
            _handle = IntPtr.Zero;
            CudaException.ThrowIfError(CudaDriverApi.cuCtxDestroy_v2(handle));
        }
    }
}
