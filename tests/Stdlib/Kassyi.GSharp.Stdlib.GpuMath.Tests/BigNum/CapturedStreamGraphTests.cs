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

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly GpuBigUInt _a;
    private readonly GpuBigUInt _b;
    private readonly GpuBigUInt _result;

    public CapturedStreamGraphTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        _a = new GpuBigUInt(_workspace, Words);
        _b = new GpuBigUInt(_workspace, Words);
        _result = new GpuBigUInt(_workspace, Words);
    }

    public void Dispose()
    {
        _result.Dispose();
        _b.Dispose();
        _a.Dispose();
        _workspace.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Capture_ValidOperations_LaunchesAndReplaysWithoutDriverAllocations()
    {
        // Warm up the kernel and temp pool slot before capturing so the capture itself stays zero-allocation.
        WriteWords(_a, 1);
        WriteWords(_b, 2);
        using (var warmupTemp = new GpuBigUInt(_workspace, Words))
        {
            _workspace.Provider.Add(_workspace, _a, _b, warmupTemp);
            _workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, warmupTemp.DevicePointer, Words,
                _workspace.Stream);
        }

        _workspace.Provider.SynchronizeStream(_workspace.Stream);

        using var graph = CapturedStreamGraph.Capture(_workspace, () =>
        {
            using var temp = new GpuBigUInt(_workspace, Words);
            _workspace.Provider.Add(_workspace, _a, _b, temp);
            _workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                _workspace.Stream);
        });

        var afterCapture = _workspace.GetMemoryStatistics();
        for (var i = 1; i <= 10; i++)
        {
            var left = (BigInteger)i * 1_000_003;
            var right = (BigInteger)i * 9_999_991;
            WriteWords(_a, left);
            WriteWords(_b, right);

            graph.Launch();
            _workspace.Provider.SynchronizeStream(_workspace.Stream);

            Assert.Equal(left + right, _result.ToBigInteger());
        }

        var afterReplay = _workspace.GetMemoryStatistics();
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
        _workspace.Provider.SynchronizeStream(_workspace.Stream);

        Assert.Equal(0, allocated);
    }

    // mutant: CapturedStreamGraph.cs:143 `_manager.ReturnDeferred(_captured.Buffers);`
    // -> omitted (DeferredBuffers stays high)
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Dispose_AfterCapture_DestroysGraphAndReturnsDeferredBuffers()
    {
        var start = _workspace.GetMemoryStatistics();
        var graph = CaptureWithDeferredTemp(_workspace, _a, _b, _result);
        var compiled = _workspace.GetMemoryStatistics();

        graph.Dispose();
        var disposed = _workspace.GetMemoryStatistics();

        Assert.True(compiled.DeferredBuffers > start.DeferredBuffers);
        Assert.Equal(start.DeferredBuffers, disposed.DeferredBuffers);
        Assert.True(
            disposed.PooledBuffers - compiled.PooledBuffers >= compiled.DeferredBuffers - start.DeferredBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var graph = CaptureWithDeferredTemp(_workspace, _a, _b, _result);

        graph.Dispose();
        var afterFirst = _workspace.GetMemoryStatistics();

        graph.Dispose();
        var afterSecond = _workspace.GetMemoryStatistics();

        Assert.Equal(afterFirst, afterSecond);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Launch_AfterDispose_ThrowsObjectDisposedException()
    {
        var graph = CaptureWithDeferredTemp(_workspace, _a, _b, _result);
        graph.Dispose();

        Assert.Throws<ObjectDisposedException>(graph.Launch);
    }

    // mutant: CapturedStreamGraph.cs:177
    // `RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),` -> omitted
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyThrowsAfterFreeingTemp_AbortsAndReturnsDeferredBuffers()
    {
        var start = _workspace.GetMemoryStatistics();
        var expected = new InvalidOperationException("Simulated capture failure.");

        var actual = Assert.Throws<InvalidOperationException>(() =>
            CapturedStreamGraph.Capture(_workspace, () =>
            {
                using var temp = new GpuBigUInt(_workspace, Words);
                _workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                    _workspace.Stream);
                throw expected;
            }));
        var failed = _workspace.GetMemoryStatistics();

        Assert.Same(expected, actual);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));

        WriteWords(_a, 11);
        WriteWords(_b, 31);
        _workspace.Provider.Add(_workspace, _a, _b, _result);
        _workspace.Provider.SynchronizeStream(_workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    // mutant: CapturedStreamGraph.cs:179
    // `ThrowIfCleanupFailed("CUDA graph capture failed, and so did its cleanup.", original, cleanupErrors);`
    // -> omitted
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_BodyInvalidatesStreamAndThrows_ThrowsAggregateException()
    {
        var start = _workspace.GetMemoryStatistics();
        Exception? original = null;

        var error = Assert.Throws<AggregateException>(() =>
            CapturedStreamGraph.Capture(_workspace, () =>
            {
                using (var temp = new GpuBigUInt(_workspace, Words))
                {
                    _workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                        _workspace.Stream);
                }

                try
                {
                    _workspace.Provider.ReadWord(_a.DevicePointer, 0, _workspace.Stream);
                }
                catch (Exception ex)
                {
                    original = ex;
                    throw;
                }
            }));
        var failed = _workspace.GetMemoryStatistics();

        Assert.NotNull(original);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Same(original, error.InnerExceptions[0]);
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (failed.DeferredBuffers, failed.LiveBuffers));

        WriteWords(_a, 19);
        WriteWords(_b, 23);
        _workspace.Provider.Add(_workspace, _a, _b, _result);
        _workspace.Provider.SynchronizeStream(_workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_NestedOrDuringActiveDeferral_ThrowsInvalidOperationException()
    {
        var start = _workspace.GetMemoryStatistics();

        using (var outer = CapturedStreamGraph.Capture(_workspace, () =>
        {
            using var temp = new GpuBigUInt(_workspace, Words);
            _workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words,
                _workspace.Stream);
            Assert.Throws<InvalidOperationException>(() =>
                CapturedStreamGraph.Capture(_workspace, static () => { }));
        }))
        {
            outer.Launch();
            _workspace.Provider.SynchronizeStream(_workspace.Stream);
        }

        var manager = (DefaultGpuMemoryManager)_workspace.MemoryManager;
        var deferral = manager.BeginDeferral();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                CapturedStreamGraph.Capture(_workspace, static () => { }));
        }
        finally
        {
            manager.ReturnDeferred(manager.EndDeferral(deferral).Buffers);
        }

        var end = _workspace.GetMemoryStatistics();
        Assert.Equal((start.DeferredBuffers, start.LiveBuffers), (end.DeferredBuffers, end.LiveBuffers));
    }

    // mutant: CapturedStreamGraph.cs:135 `using (Workspace.ActivateContext())`
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

        public IntPtr DestroyCurrentContext { get; private set; }

        public int DeferredDuringDestroy { get; private set; }

        public void BeginCapture(IntPtr stream) => _inner.BeginCapture(stream);

        public IntPtr EndCapture(IntPtr stream) => _inner.EndCapture(stream);

        public IntPtr InstantiateGraph(IntPtr graph) => _inner.InstantiateGraph(graph);

        public void LaunchGraph(IntPtr graphExec, IntPtr stream) => _inner.LaunchGraph(graphExec, stream);

        public void DestroyGraph(IntPtr graph, IntPtr graphExec)
        {
            CudaDriverApi.cuCtxGetCurrent(out var current);
            DestroyCurrentContext = current;
            DeferredDuringDestroy = workspace.GetMemoryStatistics().DeferredBuffers;
            _inner.DestroyGraph(graph, graphExec);
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
