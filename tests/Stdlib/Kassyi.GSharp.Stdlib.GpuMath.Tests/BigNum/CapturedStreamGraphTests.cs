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

    // mutant: CapturedStreamGraph.cs:286 `_manager.ReturnDeferred(_captured.Buffers);`
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

    // mutant: CapturedStreamGraph.cs:320
    // `RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),` -> omitted
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyThrowsAfterFreeingTemp_AbortsAndReturnsDeferredBuffers()
    {
        var start = Workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated capture failure.");

        var actual = Assert.Throws<InvalidOperationException>(() => CaptureThrowingWithTemp(expected));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Same(expected, actual);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));

        WriteWords(_a, 11);
        WriteWords(_b, 31);
        Workspace.Provider.Add(Workspace, _a, _b, _result);
        Workspace.Provider.SynchronizeStream(Workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    // mutant: CapturedStreamGraph.cs:322
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

    // mutant: CapturedStreamGraph.cs:322
    // `ThrowIfCleanupFailed("CUDA graph capture failed, and so did its cleanup.", original, cleanupErrors);`
    // -> omitted
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyThrowsAndEndingTheCaptureFails_ThrowsAggregateException()
    {
        var start = Workspace.GetMemoryStatistics();
        var original = new InvalidOperationException("Simulated capture failure.");
        var cleanupError = new InvalidOperationException("Simulated end-capture failure.");
        var observer = new ObservingGraphProvider(Workspace) { EndCaptureException = cleanupError };

        var error = Assert.Throws<AggregateException>(() => CaptureThrowingWithTemp(original, observer));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Same(original, error.InnerExceptions[0]);
        Assert.Same(cleanupError, error.InnerExceptions[1]);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));
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

    // mutant: CapturedStreamGraph.cs:278 `using (Workspace.ActivateContext())`
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
    // mutant: CapturedStreamGraph.cs:334
    // `RunCleanupStep((graphProvider, graph), static s => s.graphProvider.DestroyGraph(s.graph, IntPtr.Zero),`
    // -> omitted (DestroyCallCount stays 0)
    // mutant: CapturedStreamGraph.cs:338
    // `RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),`
    // -> omitted (DeferredBuffers stays high)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_InstantiateGraphThrows_DestroysGraphAndReturnsDeferredBuffers()
    {
        var start = Workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated instantiate failure.");
        var observer = new ObservingGraphProvider(Workspace) { InstantiateException = expected };

        var actual = Assert.Throws<InvalidOperationException>(() => CaptureWithTempCopy(observer));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Same(expected, actual);
        Assert.NotEqual(IntPtr.Zero, observer.LastCapturedGraph);
        Assert.Equal(observer.LastCapturedGraph, observer.DestroyedGraph);
        Assert.Equal(1, observer.DestroyCallCount);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));
    }

    // When both InstantiateGraph and DestroyGraph throw, ReleaseAfterFailure must still return deferred buffers and
    // combine both exceptions into an AggregateException with the original first.
    // mutant: CapturedStreamGraph.cs:340
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

        var error = Assert.Throws<AggregateException>(() => CaptureWithTempCopy(observer));
        var failed = Workspace.GetMemoryStatistics();

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Same(original, error.InnerExceptions[0]);
        Assert.Same(cleanupError, error.InnerExceptions[1]);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));
    }

    // When EndCapture throws, RecordStream must release deferred buffers via ReleaseAfterFailure without leaking.
    // mutant: CapturedStreamGraph.cs:222
    // `ReleaseAfterFailure(manager, graphProvider, manager.EndDeferral(deferral), graph, original);`
    // -> omitted (deferral stays active and DeferredBuffers leaks)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_EndCaptureThrows_ReturnsDeferredBuffersAndRestoresWorkspace()
    {
        var start = Workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated end-capture failure.");
        var observer = new ObservingGraphProvider(Workspace) { EndCaptureException = expected };

        var actual = Assert.Throws<InvalidOperationException>(() => CaptureWithTempCopy(observer));
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

    private CapturedStreamGraph CaptureWithTempCopy(IGpuGraphProvider graphProvider) =>
        CapturedStreamGraph.Capture(Workspace, () =>
        {
            using var temp = new GpuBigUInt(Workspace, Words);
            Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words, Workspace.Stream);
        }, graphProvider);

    private CapturedStreamGraph CaptureThrowingWithTemp(Exception error, IGpuGraphProvider? graphProvider = null) =>
        CapturedStreamGraph.Capture(Workspace, () =>
        {
            using var temp = new GpuBigUInt(Workspace, Words);
            Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words, Workspace.Stream);
            throw error;
        }, graphProvider ?? Workspace.Provider);

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
    // mutant: CapturedStreamGraph.cs:366 `errors.Insert(0, original);`
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
