#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Dedicated capture-stream isolation, zero-clear capture, and post-abort recovery tests for
/// <see cref="CapturedStreamGraph"/>.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class CapturedStreamGraphIsolationTests : IDisposable
{
    private const int Words = 8;

    private readonly GpuTestWorkspace _testWorkspace;
    private readonly GpuBigUInt _a;
    private readonly GpuBigUInt _b;
    private readonly GpuBigUInt _result;

    public CapturedStreamGraphIsolationTests()
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
    [Scenario(TestScenario.Boundary)]
    public void CompileToGraph_LegacyCopyOnAnotherThreadDuringCapture_BothSucceed()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name = nameof(CompileToGraph_LegacyCopyOnAnotherThreadDuringCapture_BothSucceed);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(CapturedStreamGraphIsolationTests), Name);
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
            // mutant: CapturedStreamGraph.cs:182 `graphProvider.BeginCapture(captureStream);`
            // -> `graphProvider.BeginCapture(workspace.BaseStream);`
            // mutant: CapturedStreamGraph.cs:11 `private const uint NonBlockingStreamFlag = 1;`
            // -> `private const uint NonBlockingStreamFlag = 0;`
            // mutant: GpuWorkspace.cs:56 `public IntPtr Stream => _manager?.CurrentStream ?? _stream;`
            // -> `public IntPtr Stream => _stream;`
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
            // mutant: DefaultGpuMemoryManager.cs:95
            // `provider.ClearMemoryAsync(ptr, numWords, session?.Stream ?? stream);`
            // -> `provider.ClearMemoryAsync(ptr, numWords, stream);`
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
        // mutant: DefaultGpuMemoryManager.cs:216
        // `provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, session.Stream);`
        // -> `provider.ClearMemoryAsync(buffer.Pointer, buffer.TotalWords, stream);`
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
        // mutant: CapturedStreamGraph.cs:306 `graph = graphProvider.EndCapture(deferral.Stream);`
        // -> `graph = graphProvider.EndCapture(workspace.BaseStream);`
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
        using var nextGraph = CapturedStreamGraph.Capture(Workspace, () =>
        {
            using var temp = new GpuBigUInt(Workspace, Words);
            Workspace.Provider.Add(Workspace, _a, _b, temp);
            Workspace.Provider.CopyDeviceToDevice(_result.DevicePointer, temp.DevicePointer, Words, Workspace.Stream);
        });
        nextGraph.Launch();
        Workspace.Provider.SynchronizeStream(Workspace.Stream);
        Assert.Equal(new BigInteger(42), _result.ToBigInteger());
    }

    private static void WriteWords(GpuBigUInt target, BigInteger value)
    {
        var words = new uint[target.NumWords];
        value.WriteLimbs(words);
        target.Workspace.Provider.CopyHostToDevice(target.DevicePointer, words, target.NumWords,
            target.Workspace.Stream);
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
