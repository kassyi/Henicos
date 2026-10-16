using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Validates device-side stream gating (<see cref="CudaStreamGate"/>) and inter-stream event dependencies
/// (<see cref="CudaStreamExtensions.WaitFor"/>).
/// </summary>
[Collection(TestCollections.DeviceExclusive)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class CudaStreamGateTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Enqueue_WhenClosed_HoldsStreamUntilOpen()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();
            Assert.False(gate.IsOpen);

            gate.Enqueue(stream.Handle);

            var pending = stream.SynchronizeAsync().AsTask();
            Assert.False(pending.IsCompleted, "The stream must remain held while the gate is closed.");
            Assert.Equal(
                CUresult.CudaErrorNotReady,
                CudaTestEnvironment.InContext(() => CudaDriverApi.cuStreamQuery(stream.Handle)));

            gate.Open();
            Assert.True(gate.IsOpen);

            await pending.WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Enqueue_StagingBufferCopyAfterGate_ObservesDataWrittenBeforeOpen()
    {
        CudaCapability.EnsureGpuAvailable();

        const uint InitialValue = 0x11111111u;
        const uint UpdatedValue = 0xCAFEBABEu;

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        var pinnedHost = IntPtr.Zero;
        var deviceBuffer = IntPtr.Zero;
        try
        {
            CudaTestEnvironment.InContext(() =>
            {
                // CU_MEMHOSTALLOC_PORTABLE = 0x01
                CudaException.ThrowIfError(CudaDriverApi.cuMemHostAlloc(out pinnedHost, sizeof(uint), 0x01));
                CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out deviceBuffer, sizeof(uint)));
                unsafe
                {
                    Volatile.Write(ref *(uint*)pinnedHost, InitialValue);
                }
            });

            using var gate = new CudaStreamGate();
            gate.Enqueue(stream.Handle);

            CudaTestEnvironment.InContext(() =>
            {
                CudaException.ThrowIfError(
                    CudaDriverApi.cuMemcpyHtoDAsync_v2(deviceBuffer, pinnedHost, sizeof(uint), stream.Handle));
            });

            // Mutate the pinned staging buffer on the host while the copy is held behind the gate on the device.
            unsafe
            {
                Volatile.Write(ref *(uint*)pinnedHost, UpdatedValue);
            }

            gate.Open();
            await stream.SynchronizeAsync().AsTask().WaitAsync(CompletionTimeout);

            var readback = new uint[1];
            await CudaHostTransfer.CopyToHostAsync(deviceBuffer, readback.AsMemory(), stream.Handle)
                .AsTask().WaitAsync(CompletionTimeout);

            Assert.Equal(UpdatedValue, readback[0]);
        }
        finally
        {
            CudaTestEnvironment.InContext(() =>
            {
                stream.Dispose();
                if (deviceBuffer != IntPtr.Zero)
                {
                    CudaDriverApi.cuMemFree_v2(deviceBuffer);
                }

                if (pinnedHost != IntPtr.Zero)
                {
                    CudaDriverApi.cuMemFreeHost(pinnedHost);
                }
            });
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Enqueue_WhenAlreadyOpen_DoesNotHoldStream()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();
            gate.Open();
            Assert.True(gate.IsOpen);

            gate.Enqueue(stream.Handle);
            await stream.SynchronizeAsync().AsTask().WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Enqueue_MultipleStreamsOnSameGate_AllProceedOnSingleOpen()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream1 = CudaTestEnvironment.InContext(static () => new CudaStream());
        var stream2 = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();
            gate.Enqueue(stream1.Handle);
            gate.Enqueue(stream2.Handle);

            var wait1 = stream1.SynchronizeAsync().AsTask();
            var wait2 = stream2.SynchronizeAsync().AsTask();
            Assert.False(wait1.IsCompleted);
            Assert.False(wait2.IsCompleted);

            gate.Open();
            await Task.WhenAll(wait1, wait2).WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(() =>
            {
                stream1.Dispose();
                stream2.Dispose();
            });
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Enqueue_DoesNotStallHostFunctionsOnOtherStreams()
    {
        CudaCapability.EnsureGpuAvailable();

        var gatedStream = CudaTestEnvironment.InContext(static () => new CudaStream());
        var freeStream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();
            gate.Enqueue(gatedStream.Handle);
            var gatedWait = gatedStream.SynchronizeAsync().AsTask();

            // WaitAsync uses cuLaunchHostFunc on freeStream. Because CudaStreamGate waits on the device rather than
            // inside a host callback, freeStream's host function must complete while gatedStream stays held.
            await freeStream.SynchronizeAsync().AsTask().WaitAsync(CompletionTimeout);
            Assert.False(gate.IsOpen);
            Assert.False(gatedWait.IsCompleted);

            gate.Open();
            await gatedWait.WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(() =>
            {
                gatedStream.Dispose();
                freeStream.Dispose();
            });
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Enqueue_StreamOfIsolatedContext_WaitsAndRestoresCurrentContext()
    {
        CudaCapability.EnsureGpuAvailable();

        var isolatedContext = CudaTestEnvironment.InContext(static () =>
        {
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
            CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out var ctx, 0, device));
            CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _));
            return ctx;
        });

        var stream = IntPtr.Zero;
        try
        {
            using (new GpuKernelLoader.CudaContextScope(isolatedContext))
            {
                CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out stream, 1));
            }

            using var gate = new CudaStreamGate();

            var currentAfterEnqueue = CudaTestEnvironment.InContext(() =>
            {
                gate.Enqueue(stream);
                CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out var current));
                return current;
            });

            Assert.Equal(GpuKernelLoader.GlobalContext, currentAfterEnqueue);

            var pending = CudaStreamCompletion.WaitAsync(stream).AsTask();
            Assert.False(pending.IsCompleted);

            gate.Open();
            await pending.WaitAsync(CompletionTimeout);
        }
        finally
        {
            if (stream != IntPtr.Zero)
            {
                using (new GpuKernelLoader.CudaContextScope(isolatedContext))
                {
                    CudaDriverApi.cuStreamDestroy(stream);
                }
            }

            CudaDriverApi.cuCtxDestroy_v2(isolatedContext);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task WaitFor_ConsumerWaitsForProducerWork()
    {
        CudaCapability.EnsureGpuAvailable();

        var producer = CudaTestEnvironment.InContext(static () => new CudaStream());
        var consumer = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();
            gate.Enqueue(producer.Handle);

            consumer.WaitFor(producer);

            var consumerWait = consumer.SynchronizeAsync().AsTask();
            Assert.False(consumerWait.IsCompleted, "Consumer stream must wait until producer reaches the recorded event.");

            gate.Open();
            await consumerWait.WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(() =>
            {
                consumer.Dispose();
                producer.Dispose();
            });
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public async Task Close_AfterOpen_RearmsGateAcrossMultipleGenerations()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();

            for (var cycle = 0; cycle < 8; cycle++)
            {
                Assert.False(gate.IsOpen);
                gate.Enqueue(stream.Handle);

                var pending = stream.SynchronizeAsync().AsTask();
                Assert.False(pending.IsCompleted, $"Stream must be held on cycle {cycle} before Open().");

                gate.Open();
                Assert.True(gate.IsOpen);
                await pending.WaitAsync(CompletionTimeout);

                gate.Close();
                Assert.False(gate.IsOpen);
            }
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public async Task Open_And_Close_AreIdempotent()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            using var gate = new CudaStreamGate();

            // Redundant Close() while already closed must not advance the generation past the next Open().
            gate.Close();
            gate.Close();
            Assert.False(gate.IsOpen);

            gate.Enqueue(stream.Handle);
            var pending = stream.SynchronizeAsync().AsTask();
            Assert.False(pending.IsCompleted);

            // Redundant Open() calls must be harmless.
            gate.Open();
            gate.Open();
            Assert.True(gate.IsOpen);

            await pending.WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public async Task Dispose_WhileStreamWaiting_ReleasesStreamWithoutDeadlock()
    {
        CudaCapability.EnsureGpuAvailable();

        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            var gate = new CudaStreamGate();
            gate.Enqueue(stream.Handle);
            var pending = stream.SynchronizeAsync().AsTask();
            Assert.False(pending.IsCompleted);

            // Disposing an unopened gate must unblock any waiting stream before freeing the mapped host memory.
            gate.Dispose();
            await pending.WaitAsync(CompletionTimeout);
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void WaitFor_SameStream_SucceedsAsNoOp()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();

        stream.WaitFor(stream);
        stream.Synchronize();
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_InCpuFallbackMode_ThrowsNotSupportedException()
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            Assert.Throws<NotSupportedException>(static () => new CudaStreamGate());
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Enqueue_OnCapturingStream_ThrowsInvalidOperationException()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();
        using var gate = new CudaStreamGate();

        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream.Handle, 1));
        try
        {
            Assert.Throws<InvalidOperationException>(() => gate.Enqueue(stream.Handle));
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

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Methods_AfterDispose_ThrowObjectDisposedException()
    {
        CudaCapability.EnsureGpuAvailable();

        var gate = new CudaStreamGate();
        gate.Dispose();
        gate.Dispose(); // Double dispose must be safe.

        Assert.Throws<ObjectDisposedException>(() => gate.Enqueue(IntPtr.Zero));
        Assert.Throws<ObjectDisposedException>(gate.Open);
        Assert.Throws<ObjectDisposedException>(gate.Close);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void WaitFor_NullArguments_ThrowArgumentNullException()
    {
        CudaCapability.EnsureGpuAvailable();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var stream = new CudaStream();

        // Intentionally passing null to assert ArgumentNullException guard
        Assert.Throws<ArgumentNullException>(() => CudaStreamExtensions.WaitFor(null!, stream));
        // Intentionally passing null to assert ArgumentNullException guard
        Assert.Throws<ArgumentNullException>(() => stream.WaitFor(null!));
    }
}
