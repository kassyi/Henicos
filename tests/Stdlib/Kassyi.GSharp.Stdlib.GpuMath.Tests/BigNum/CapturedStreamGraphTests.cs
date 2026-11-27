#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Unit tests for <see cref="CapturedStreamGraph"/> capture, steady-state launch, disposal, and failure cleanup.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class CapturedStreamGraphTests : IDisposable
{
    private const int Words = 8;

    private readonly GpuTestWorkspace _testWorkspace;
    private readonly GpuBigUInt _a;
    private readonly GpuBigUInt _b;
    private readonly GpuBigUInt _result;

    public CapturedStreamGraphTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _testWorkspace = new GpuTestWorkspace(64, GpuExecutionEngine.GSharpAot);
        GpuBigUInt a = default;
        GpuBigUInt b = default;
        GpuBigUInt result = default;
        try
        {
            a = new GpuBigUInt(_testWorkspace.Workspace, Words);
            b = new GpuBigUInt(_testWorkspace.Workspace, Words);
            result = new GpuBigUInt(_testWorkspace.Workspace, Words);
            _a = a;
            _b = b;
            _result = result;
        }
        catch
        {
            result.Dispose();
            b.Dispose();
            a.Dispose();
            _testWorkspace.Dispose();
            throw;
        }
    }

    private GpuWorkspace Workspace => _testWorkspace.Workspace;

    public void Dispose()
    {
        _result.Dispose();
        _b.Dispose();
        _a.Dispose();
        _testWorkspace.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Capture_ValidOperations_LaunchesAndReplaysWithoutDriverAllocations()
    {
        // Warm up the kernel and temp pool slot before capturing so the capture itself stays zero-allocation.
        WriteWords(_a, 1);
        WriteWords(_b, 2);
        using (var warmupTemp = new GpuBigUInt(Workspace, Words))
        {
            Workspace.Provider.Add(Workspace, _a, _b, warmupTemp);
            Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, warmupTemp.DevicePointer, Words,
                Workspace.Stream);
        }

        Workspace.Provider.SynchronizeStream(Workspace.Stream);

        using var graph = CapturedStreamGraph.Capture(Workspace, () =>
        {
            using var temp = new GpuBigUInt(Workspace, Words);
            Workspace.Provider.Add(Workspace, _a, _b, temp);
            Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                Workspace.Stream);
        });

        var afterCapture = Workspace.GetMemoryStatistics();
        for (var i = 1; i <= 10; i++)
        {
            var left = (BigInteger)i * 1_000_003;
            var right = (BigInteger)i * 9_999_991;
            WriteWords(_a, left);
            WriteWords(_b, right);

            graph.Launch();
            Workspace.Provider.SynchronizeStream(Workspace.Stream);

            Assert.Equal(left + right, _result.ToBigInteger());
        }

        var afterReplay = Workspace.GetMemoryStatistics();
        Assert.Equal(afterCapture.DriverAllocations, afterReplay.DriverAllocations);
        Assert.True(afterCapture.DeferredBuffers > 0);

        for (var i = 0; i < 16; i++)
        {
            graph.Launch();
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 64; i++)
        {
            graph.Launch();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Workspace.Provider.SynchronizeStream(Workspace.Stream);

        Assert.Equal(0, allocated);
    }

    // mutant: CapturedStreamGraph.cs:231 `_manager.ReturnDeferred(_captured.Buffers);`
    // -> omitted (DeferredBuffers stays high)
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Dispose_AfterCapture_DestroysGraphAndReturnsDeferredBuffers()
    {
        var start = Workspace.GetMemoryStatistics();
        var graph = CaptureWithDeferredTemp(Workspace, _a, _b, _result);
        var compiled = Workspace.GetMemoryStatistics();

        graph.Dispose();
        var disposed = Workspace.GetMemoryStatistics();

        Assert.True(compiled.DeferredBuffers > start.DeferredBuffers);
        Assert.Equal(start.DeferredBuffers, disposed.DeferredBuffers);
        Assert.True(
            disposed.PooledBuffers - compiled.PooledBuffers >= compiled.DeferredBuffers - start.DeferredBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var graph = CaptureWithDeferredTemp(Workspace, _a, _b, _result);

        graph.Dispose();
        var afterFirst = Workspace.GetMemoryStatistics();

        graph.Dispose();
        var afterSecond = Workspace.GetMemoryStatistics();

        Assert.Equal(afterFirst, afterSecond);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Launch_AfterDispose_ThrowsObjectDisposedException()
    {
        var graph = CaptureWithDeferredTemp(Workspace, _a, _b, _result);
        graph.Dispose();

        Assert.Throws<ObjectDisposedException>(graph.Launch);
    }

    // mutant: CapturedStreamGraph.cs:265
    // `RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),` -> omitted
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyThrowsAfterFreeingTemp_AbortsAndReturnsDeferredBuffers()
    {
        var start = Workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated capture failure.");

        var actual = Assert.Throws<InvalidOperationException>(() =>
            CapturedStreamGraph.Capture(Workspace, () =>
            {
                using var temp = new GpuBigUInt(Workspace, Words);
                Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                    Workspace.Stream);
                throw expected;
            }));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Same(expected, actual);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));

        WriteWords(_a, 11);
        WriteWords(_b, 31);
        Workspace.Provider.Add(Workspace, _a, _b, _result);
        Workspace.Provider.SynchronizeStream(Workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    // mutant: CapturedStreamGraph.cs:267
    // `ThrowIfCleanupFailed("CUDA graph capture failed, and so did its cleanup.", original, cleanupErrors);`
    // -> omitted
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyInvalidatesStreamAndThrows_ThrowsAggregateException()
    {
        var start = Workspace.GetMemoryStatistics();
        Exception? original = null;

        var error = Assert.Throws<AggregateException>(() =>
            CapturedStreamGraph.Capture(Workspace, () =>
            {
                using (var temp = new GpuBigUInt(Workspace, Words))
                {
                    Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                        Workspace.Stream);
                }

                try
                {
                    Workspace.Provider.ReadWord(_a.DevicePointer, 0, Workspace.Stream);
                }
                catch (Exception ex)
                {
                    original = ex;
                    throw;
                }
            }));
        var failed = Workspace.GetMemoryStatistics();

        Assert.NotNull(original);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Same(original, error.InnerExceptions[0]);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));

        WriteWords(_a, 19);
        WriteWords(_b, 23);
        Workspace.Provider.Add(Workspace, _a, _b, _result);
        Workspace.Provider.SynchronizeStream(Workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_NestedOrDuringActiveDeferral_ThrowsInvalidOperationException()
    {
        var start = Workspace.GetMemoryStatistics();

        using (var outer = CapturedStreamGraph.Capture(Workspace, () =>
        {
            using var temp = new GpuBigUInt(Workspace, Words);
            Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                Workspace.Stream);
            Assert.Throws<InvalidOperationException>(() =>
                CapturedStreamGraph.Capture(Workspace, static () => { }));
        }))
        {
            outer.Launch();
            Workspace.Provider.SynchronizeStream(Workspace.Stream);
        }

        var manager = (DefaultGpuMemoryManager)Workspace.MemoryManager;
        var deferral = manager.BeginDeferral();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                CapturedStreamGraph.Capture(Workspace, static () => { }));
        }
        finally
        {
            manager.ReturnDeferred(manager.EndDeferral(deferral).Buffers);
        }

        var end = Workspace.GetMemoryStatistics();
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (end.DeferredBuffers, end.LiveBuffers));
    }

    // mutant: CapturedStreamGraph.cs:223 `using (Workspace.ActivateContext())`
    // -> omitted (pool thread lacks isolated context)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public async Task Dispose_OnPoolThreadWithIsolatedContext_ActivatesContextAndReleasesBuffers()
    {
        using var isolated = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot, useIsolatedContext: true);
        GpuBigUInt src;
        GpuBigUInt dst;
        using (isolated.ActivateContext())
        {
            src = new GpuBigUInt(isolated, Words);
            dst = new GpuBigUInt(isolated, Words);
        }

        try
        {
            var start = isolated.GetMemoryStatistics();
            var observer = new ObservingGraphProvider(isolated);
            var graph = CapturedStreamGraph.Capture(isolated, () =>
            {
                using var temp = new GpuBigUInt(isolated, Words);
                isolated.Provider.CopyDeviceToDevice(temp.DevicePointer, src.DevicePointer, Words, isolated.Stream);
                isolated.Provider.CopyDeviceToDevice(dst.DevicePointer, temp.DevicePointer, Words, isolated.Stream);
            }, observer);
            var compiled = isolated.GetMemoryStatistics();

            await Task.Run(graph.Dispose);
            var disposed = isolated.GetMemoryStatistics();

            Assert.Equal(isolated.IsolatedContext.Handle, observer.DestroyCurrentContext);
            Assert.True(observer.DeferredDuringDestroy > start.DeferredBuffers);
            Assert.True(compiled.DeferredBuffers > start.DeferredBuffers);
            Assert.Equal(start.DeferredBuffers, disposed.DeferredBuffers);
            Assert.True(
                disposed.PooledBuffers - compiled.PooledBuffers >= compiled.DeferredBuffers - start.DeferredBuffers);
        }
        finally
        {
            using (isolated.ActivateContext())
            {
                dst.Dispose();
                src.Dispose();
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_AfterWorkspaceDispose_DoesNotDoubleFree()
    {
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var src = new GpuBigUInt(workspace, Words);
        var dst = new GpuBigUInt(workspace, Words);
        var graph = CaptureWithDeferredTemp(workspace, src, src, dst);
        var compiled = workspace.GetMemoryStatistics();

        src.Dispose();
        dst.Dispose();
        workspace.Dispose();
        var afterWorkspace = workspace.GetMemoryStatistics();

        graph.Dispose();
        var afterGraph = workspace.GetMemoryStatistics();

        Assert.True(compiled.DeferredBuffers > 0);
        Assert.Equal(
            (0, 0, 0),
            (afterWorkspace.DeferredBuffers, afterWorkspace.LiveBuffers, afterWorkspace.PooledBuffers));
        Assert.Equal(afterWorkspace, afterGraph);
        Assert.Equal(afterWorkspace.DriverAllocations, afterWorkspace.DriverFrees);
    }

    // ReleaseAfterFailure must destroy the partial graph and return deferred buffers when InstantiateGraph throws.
    // mutant: CapturedStreamGraph.cs:279
    // `RunCleanupStep((graphProvider, graph), static s => s.graphProvider.DestroyGraph(s.graph, IntPtr.Zero),`
    // -> omitted (DestroyCallCount stays 0)
    // mutant: CapturedStreamGraph.cs:283
    // `RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),`
    // -> omitted (DeferredBuffers stays high)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_InstantiateGraphThrows_DestroysGraphAndReturnsDeferredBuffers()
    {
        var start = Workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated instantiate failure.");
        var observer = new ObservingGraphProvider(Workspace) { InstantiateException = expected };

        var actual = Assert.Throws<InvalidOperationException>(() =>
            CapturedStreamGraph.Capture(Workspace, () =>
            {
                using var temp = new GpuBigUInt(Workspace, Words);
                Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                    Workspace.Stream);
            }, observer));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Same(expected, actual);
        Assert.NotEqual(IntPtr.Zero, observer.LastCapturedGraph);
        Assert.Equal(observer.LastCapturedGraph, observer.DestroyedGraph);
        Assert.Equal(1, observer.DestroyCallCount);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));
    }

    // When both InstantiateGraph and DestroyGraph throw, ReleaseAfterFailure must still return deferred buffers and
    // combine both exceptions into an AggregateException with the original first.
    // mutant: CapturedStreamGraph.cs:285
    // `ThrowIfCleanupFailed("Creating the CUDA graph failed, and so did its cleanup.", original, cleanupErrors);`
    // -> omitted (only original InvalidOperationException is thrown)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_InstantiateAndDestroyThrow_ThrowsAggregateExceptionAndReturnsDeferredBuffers()
    {
        var start = Workspace.GetMemoryStatistics();
        var original = new InvalidOperationException("Simulated instantiate failure.");
        var cleanupError = new InvalidOperationException("Simulated destroy failure.");
        var observer = new ObservingGraphProvider(Workspace)
        {
            InstantiateException = original,
            DestroyException = cleanupError,
        };

        var error = Assert.Throws<AggregateException>(() =>
            CapturedStreamGraph.Capture(Workspace, () =>
            {
                using var temp = new GpuBigUInt(Workspace, Words);
                Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                    Workspace.Stream);
            }, observer));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Same(original, error.InnerExceptions[0]);
        Assert.Same(cleanupError, error.InnerExceptions[1]);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));
    }

    // When EndCapture throws, RecordStream must release deferred buffers via ReleaseAfterFailure without leaking.
    // mutant: CapturedStreamGraph.cs:167
    // `ReleaseAfterFailure(manager, graphProvider, manager.EndDeferral(deferral), graph, original);`
    // -> omitted (deferral stays active and DeferredBuffers leaks)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_EndCaptureThrows_ReturnsDeferredBuffersAndRestoresWorkspace()
    {
        var start = Workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated end-capture failure.");
        var observer = new ObservingGraphProvider(Workspace) { EndCaptureException = expected };

        var actual = Assert.Throws<InvalidOperationException>(() =>
            CapturedStreamGraph.Capture(Workspace, () =>
            {
                using var temp = new GpuBigUInt(Workspace, Words);
                Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                    Workspace.Stream);
            }, observer));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Same(expected, actual);
        Assert.Equal(0, observer.DestroyCallCount);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));

        WriteWords(_a, 17);
        WriteWords(_b, 25);
        Workspace.Provider.Add(Workspace, _a, _b, _result);
        Workspace.Provider.SynchronizeStream(Workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CompileToGraph_LegacyCopyOnAnotherThreadDuringCapture_BothSucceed()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name = nameof(CompileToGraph_LegacyCopyOnAnotherThreadDuringCapture_BothSucceed);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(CapturedStreamGraphTests), Name);
            return;
        }

        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigInt(new GpuBigUInt(workspace, Words), isNegative: false);
        using var b = new GpuBigInt(new GpuBigUInt(workspace, Words), isNegative: false);
        using var c = new GpuBigInt(new GpuBigUInt(workspace, Words), isNegative: false);
        WriteWords(a.Value, new BigInteger(1_000_003));
        WriteWords(b.Value, new BigInteger(7_919));
        WriteWords(c.Value, new BigInteger(42));

        using var start = new ManualResetEventSlim(false);
        using var done = new ManualResetEventSlim(false);
        Exception? otherThreadError = null;
        var hostBuffer = new int[16];

        var otherThread = new Thread(() =>
        {
            start.Wait();
            try
            {
                using var scope = new GpuKernelLoader.CudaContextScope();
                using var deviceBuffer = new GpuBuffer<int>(16);
                deviceBuffer.CopyToHost(hostBuffer);
            }
            catch (Exception ex)
            {
                otherThreadError = ex;
            }
            finally
            {
                done.Set();
            }
        });
        otherThread.Start();

        var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
        var observer = new LegacyCopyOnCaptureObserver(workspace, start, done);
        manager.Observer = observer;
        GpuBigIntExecutableGraph graph;
        try
        {
            graph = ((GpuBigIntExpr)a * b + c).CompileToGraph(workspace);
        }
        finally
        {
            manager.Observer = null;
            start.Set();
            otherThread.Join();
        }

        using (graph)
        {
            // Coverage witness: the legacy copy ran while CompileToGraph's capture was active.
            Assert.True(observer.Fired, "The memory observer never observed an active graph capture.");
            // CompileToGraph records on a dedicated non-blocking stream so concurrent legacy copies succeed.
            // mutant: CapturedStreamGraph.cs:141 `graphProvider.BeginCapture(captureStream);` -> `graphProvider.BeginCapture(workspace.BaseStream);`
            // mutant: CapturedStreamGraph.cs:11 `private const uint NonBlockingStreamFlag = 1;` -> `private const uint NonBlockingStreamFlag = 0;`
            // mutant: GpuWorkspace.cs:53 `public IntPtr Stream => _manager?.CurrentStream ?? _stream;` -> `public IntPtr Stream => _stream;`
            Assert.Null(otherThreadError);

            graph.Execute();
            Assert.Equal(new BigInteger(1_000_003) * 7_919 + 42, graph.Result.ToBigInteger());

            WriteWords(a.Value, new BigInteger(2_000_003));
            graph.Execute();
            Assert.Equal(new BigInteger(2_000_003) * 7_919 + 42, graph.Result.ToBigInteger());
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Capture_FreshAllocationInBody_IsZeroAtEveryLaunch()
    {
        const int FreshWords = 17;
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var output = new GpuBigUInt(workspace, 1);
        var allocsBefore = workspace.GetMemoryStatistics().DriverAllocations;
        GpuBigUInt fresh = default;
        try
        {
            using var graph = CapturedStreamGraph.Capture(workspace, () =>
            {
                fresh = workspace.RentTemp(FreshWords);
                output.CopyFrom(fresh, numWords: 1);
                workspace.Provider.WriteWord(fresh.DevicePointer, 0, 7u, workspace.Stream);
            });

            // Coverage witness: RentTemp missed the empty pool and allocated from the driver during capture.
            Assert.True(workspace.GetMemoryStatistics().DriverAllocations > allocsBefore);

            // Driver allocation inside a capture must enqueue its zero-clear on the capture stream so every launch
            // sees zeroed memory, even after a previous launch dirtied the buffer.
            // mutant: DefaultGpuMemoryManager.cs:95 `provider.ClearMemoryAsync(ptr, numWords, session?.Stream ?? stream);` -> `provider.ClearMemoryAsync(ptr, numWords, stream);`
            for (var launch = 0; launch < 2; launch++)
            {
                graph.Launch();
                workspace.Provider.SynchronizeStream(workspace.Stream);
                Assert.Equal(0u, workspace.Provider.ReadWord(output.DevicePointer, 0, workspace.Stream));
            }
        }
        finally
        {
            fresh.Dispose();
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Capture_BufferFreedAndTakenAgainInBody_IsZeroAtEveryLaunch()
    {
        const int ReuseWords = 17;
        using var output = new GpuBigUInt(Workspace, 1);
        var firstPtr = IntPtr.Zero;
        var secondPtr = IntPtr.Zero;

        using var graph = CapturedStreamGraph.Capture(Workspace, () =>
        {
            using (var first = Workspace.RentTemp(ReuseWords))
            {
                firstPtr = first.DevicePointer;
                Workspace.Provider.WriteWord(first.DevicePointer, 0, 7u, Workspace.Stream);
            }

            using var second = Workspace.RentTemp(ReuseWords);
            secondPtr = second.DevicePointer;
            output.CopyFrom(second, numWords: 1);
        });

        // Coverage witness: the second RentTemp reused the exact buffer freed earlier in the same capture.
        Assert.Equal(firstPtr, secondPtr);

        // Freeing a buffer during capture must record its zero-clear on the capture stream so reuse sees zeros.
        // mutant: DefaultGpuMemoryManager.cs:216 `provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, session.Stream);` -> `provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, stream);`
        for (var launch = 0; launch < 2; launch++)
        {
            graph.Launch();
            Workspace.Provider.SynchronizeStream(Workspace.Stream);
            Assert.Equal(0u, Workspace.Provider.ReadWord(output.DevicePointer, 0, Workspace.Stream));
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyThrows_NextCaptureOnTheWorkspaceSucceeds()
    {
        var expected = new InvalidOperationException("Simulated capture abort.");

        // AbortCapture must end capture on deferral.Stream (not the workspace base stream), so no cleanup error is
        // added and the original exception propagates intact before the next capture runs.
        // mutant: CapturedStreamGraph.cs:251 `graph = graphProvider.EndCapture(deferral.Stream);` -> `graph = graphProvider.EndCapture(workspace.BaseStream);`
        var actual = Assert.Throws<InvalidOperationException>(() =>
            CapturedStreamGraph.Capture(Workspace, () =>
            {
                using var temp = new GpuBigUInt(Workspace, Words);
                Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                    Workspace.Stream);
                throw expected;
            }));
        Assert.Same(expected, actual);

        WriteWords(_a, 13);
        WriteWords(_b, 29);
        using var nextGraph = CaptureWithDeferredTemp(Workspace, _a, _b, _result);
        nextGraph.Launch();
        Workspace.Provider.SynchronizeStream(Workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    private static CapturedStreamGraph CaptureWithDeferredTemp(GpuWorkspace workspace, GpuBigUInt a, GpuBigUInt b,
        GpuBigUInt result)
    {
        using (var warmupTemp = new GpuBigUInt(workspace, Words))
        {
            workspace.Provider.Add(workspace, a, b, warmupTemp);
            workspace.Provider.CopyDeviceToDevice(result.DevicePointer, warmupTemp.DevicePointer, Words,
                workspace.Stream);
        }

        workspace.Provider.SynchronizeStream(workspace.Stream);

        return CapturedStreamGraph.Capture(workspace, () =>
        {
            using var temp = new GpuBigUInt(workspace, Words);
            workspace.Provider.Add(workspace, a, b, temp);
            workspace.Provider.CopyDeviceToDevice(result.DevicePointer, temp.DevicePointer, Words, workspace.Stream);
        });
    }

    private static void WriteWords(GpuBigUInt target, BigInteger value)
    {
        var words = new uint[target.NumWords];
        value.WriteLimbs(words);
        target.Workspace.Provider.CopyHostToDevice(target.DevicePointer, words, target.NumWords,
            target.Workspace.Stream);
    }

    private sealed class ObservingGraphProvider(GpuWorkspace workspace) : IGpuGraphProvider
    {
        private readonly IGpuGraphProvider _inner = workspace.Provider;

        public Exception? EndCaptureException { get; set; }

        public Exception? InstantiateException { get; set; }

        public Exception? DestroyException { get; set; }

        public IntPtr LastCapturedGraph { get; private set; }

        public IntPtr DestroyedGraph { get; private set; }

        public int DestroyCallCount { get; private set; }

        public IntPtr DestroyCurrentContext { get; private set; }

        public int DeferredDuringDestroy { get; private set; }

        public void BeginCapture(IntPtr stream) => _inner.BeginCapture(stream);

        public IntPtr EndCapture(IntPtr stream)
        {
            var graph = _inner.EndCapture(stream);
            LastCapturedGraph = graph;
            if (EndCaptureException is not null)
            {
                if (graph != IntPtr.Zero)
                {
                    _inner.DestroyGraph(graph, IntPtr.Zero);
                }

                throw EndCaptureException;
            }

            return graph;
        }

        public IntPtr InstantiateGraph(IntPtr graph) =>
            InstantiateException is not null ? throw InstantiateException : _inner.InstantiateGraph(graph);

        public void LaunchGraph(IntPtr graphExec, IntPtr stream) => _inner.LaunchGraph(graphExec, stream);

        public void DestroyGraph(IntPtr graph, IntPtr graphExec)
        {
            CudaDriverApi.cuCtxGetCurrent(out var current);
            DestroyCurrentContext = current;
            DeferredDuringDestroy = workspace.GetMemoryStatistics().DeferredBuffers;
            DestroyedGraph = graph;
            DestroyCallCount++;
            _inner.DestroyGraph(graph, graphExec);
            if (DestroyException is not null)
            {
                throw DestroyException;
            }
        }
    }

    private sealed class LegacyCopyOnCaptureObserver(
        GpuWorkspace workspace,
        ManualResetEventSlim start,
        ManualResetEventSlim done) : IGpuMemoryObserver
    {
        private bool _fired;

        public bool Fired => Volatile.Read(ref _fired);

        public void Leased(IntPtr pointer, int numWords)
        {
            if (!_fired && CaptureChecks.IsCapturing(workspace))
            {
                Volatile.Write(ref _fired, true);
                start.Set();
                Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for thread B legacy copy.");
            }
        }

        public void Released(IntPtr pointer, int numWords)
        {
        }
    }
}

/// <summary>
/// CPU fallback and argument validation tests for <see cref="CapturedStreamGraph"/>.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class CapturedStreamGraphCpuFallbackTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_NullArguments_ThrowsArgumentNullException()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);

        // Intentionally passing null to assert ArgumentNullException guards.
        Assert.Throws<ArgumentNullException>(() => CapturedStreamGraph.Capture(null!, static () => { }));
        // Intentionally passing null to assert ArgumentNullException guards.
        Assert.Throws<ArgumentNullException>(() => CapturedStreamGraph.Capture(workspace, null!));
    }

    // When original is already an AggregateException, ThrowIfCleanupFailed must preserve it intact at
    // InnerExceptions[0] rather than flattening its inner exceptions.
    // mutant: CapturedStreamGraph.cs:311 `errors.Insert(0, original);`
    // -> flattened into aggregate.InnerExceptions (InnerExceptions[0] is no longer original)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ThrowIfCleanupFailed_OriginalIsAggregateException_PreservesWrapperAtIndexZero()
    {
        var leaf1 = new InvalidOperationException("Leaf 1");
        var leaf2 = new ArgumentException("Leaf 2");
        var original = new AggregateException("Original aggregate", leaf1, leaf2);
        var cleanupError = new InvalidOperationException("Cleanup failure");

        var actual = Assert.Throws<AggregateException>(() =>
            CapturedStreamGraph.ThrowIfCleanupFailed("Outer failure", original, [cleanupError]));

        Assert.Equal(2, actual.InnerExceptions.Count);
        Assert.Same(original, actual.InnerExceptions[0]);
        Assert.Same(cleanupError, actual.InnerExceptions[1]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Capture_InCpuFallback_ExecutesBodyAndLaunchDisposeAreSafeNoOps()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        var start = workspace.GetMemoryStatistics();
        var bodyCalls = 0;

        var graph = CapturedStreamGraph.Capture(workspace, () =>
        {
            bodyCalls++;
            using var temp = new GpuBigUInt(workspace, 4);
            workspace.Provider.ClearMemoryAsync(temp.DevicePointer, 4, workspace.Stream);
        });

        Assert.Equal(1, bodyCalls);
        graph.Launch();
        graph.Dispose();
        graph.Dispose();

        var disposed = workspace.GetMemoryStatistics();
        Assert.Equal(start.DeferredBuffers, disposed.DeferredBuffers);
        Assert.Throws<ObjectDisposedException>(graph.Launch);
    }
}
