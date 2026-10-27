#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Ownership of the buffers a CUDA graph uses (design 4.5.1-4.5.3): the graph owns its result and the buffers freed
/// while it was captured, owns the expression it was compiled from, and refuses to run after an operand it reads was
/// freed.
/// </summary>
/// <remarks>
/// Each test uses its own workspace and compares the allocation counters with a snapshot taken before the graph is
/// compiled. All values have <see cref="Words"/> words, so freed buffers of that size are the ones the next allocations
/// receive.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class GraphOwnershipTests : IDisposable
{
    private const int Words = 8;

    private static readonly BigInteger s_a = BigInteger.Parse("123456789012345678901234567890");
    private static readonly BigInteger s_b = BigInteger.Parse("987654321098765432109876543210");
    private static readonly BigInteger s_c = BigInteger.Parse("555555555555555555555555555555");
    private static readonly BigInteger s_d = BigInteger.Parse("111111111111111111111111111111");

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly GpuBigInt _a;
    private readonly GpuBigInt _b;
    private readonly GpuBigInt _c;
    private readonly GpuBigInt _d;

    public GraphOwnershipTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        _a = Create(_workspace, s_a);
        _b = Create(_workspace, s_b);
        _c = Create(_workspace, s_c);
        _d = Create(_workspace, s_d);
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
        _c.Dispose();
        _d.Dispose();
        _workspace.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CompiledGraph_OwnsItsResult()
    {
        // The graph owns its result (G1): Result is a view, it lives until the graph is disposed, and disposing the
        // graph returns every buffer the compilation leased.
        // mutant: GraphHandles.Destroy without freeing the result (LiveBuffers stays one higher)
        var start = _workspace.GetMemoryStatistics();
        var graph = ((GpuBigIntExpr)_a + _b).CompileToGraph(_workspace);
        graph.Execute();
        var value = Read(graph.Result);
        graph.Dispose();

        Assert.Equal(s_a + s_b, value);
        Assert.Equal(start.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
        Assert.True(ResultThrowsAfterDispose(graph));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_KeepsFreedIntermediatesOutOfThePool()
    {
        // An intermediate result freed while the graph is captured stays with the graph (G3): an allocation made after
        // the compilation never receives it, so replaying the graph cannot overwrite the new owner's value.
        // mutant: DefaultGpuMemoryManager.Free without the capture branch (the intermediate goes to the shared pool)
        var start = _workspace.GetMemoryStatistics();
        using var graph = (((GpuBigIntExpr)_a + _b) + _c).CompileToGraph(_workspace);
        var deferred = _workspace.GetMemoryStatistics().DeferredBuffers - start.DeferredBuffers;

        var pattern = Enumerable.Range(1, Words).Select(static i => 0x9E3779B9u * (uint)i).ToArray();
        using var other = new GpuBigUInt(_workspace, pattern);
        graph.Execute();
        graph.Execute();

        Assert.True(deferred >= 1, $"deferred {deferred}");
        Assert.Equal(pattern, other.ToHostArray());
        Assert.Equal(s_a + s_b + s_c, Read(graph.Result));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Capture_IntermediateReusedAsResult_IsReturnedOnce()
    {
        // In ((a + b) + c) + d the buffer of a + b is freed during the capture and taken again for the final result
        // (G3b). It must leave the deferred set when it is taken, or disposing the graph returns it twice and two later
        // allocations share it.
        // mutant: DefaultGpuMemoryManager.Allocate does not remove a reused buffer from the deferred set
        var start = _workspace.GetMemoryStatistics();
        var graph = ((((GpuBigIntExpr)_a + _b) + _c) + _d).CompileToGraph(_workspace);
        graph.Execute();
        var value = Read(graph.Result);
        graph.Dispose();

        var pointers = new HashSet<IntPtr>();
        var allocated = new List<GpuBigUInt>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var next = new GpuBigUInt(_workspace, Words);
                allocated.Add(next);
                pointers.Add(next.DevicePointer);
            }
        }
        finally
        {
            allocated.ForEach(static x => x.Dispose());
        }

        Assert.Equal(s_a + s_b + s_c + s_d, value);
        Assert.Equal(4, pointers.Count);
        Assert.Equal(start.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void GraphDispose_ReturnsDeferredBuffersToThePool()
    {
        // The graph gives the buffers freed during its capture back to the pool when it is disposed (G4, J8).
        // mutant: GraphHandles.Destroy without ReturnDeferred (DeferredBuffers does not come back)
        var start = _workspace.GetMemoryStatistics();
        var graph = (((GpuBigIntExpr)_a + _b) + _c).CompileToGraph(_workspace);
        var compiled = _workspace.GetMemoryStatistics();
        graph.Dispose();
        var disposed = _workspace.GetMemoryStatistics();

        Assert.True(compiled.DeferredBuffers > start.DeferredBuffers, $"deferred {compiled.DeferredBuffers}");
        Assert.Equal(start.DeferredBuffers, disposed.DeferredBuffers);
        Assert.True(disposed.PooledBuffers - compiled.PooledBuffers >= compiled.DeferredBuffers - start.DeferredBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void WorkspaceDispose_FreesTheBuffersOfALeftGraph()
    {
        // A graph that is never disposed keeps its buffers until the workspace is disposed, which frees them (the last
        // resort of J8); disposing the graph afterwards frees nothing twice (G5).
        // mutant: DefaultGpuMemoryManager.Dispose does not free the deferred buffers (DeferredBuffers stays above 0)
        // mutant: ReturnDeferred without the TryRemove (the late graph Dispose frees again)
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var a = Create(workspace, s_a);
        var b = Create(workspace, s_b);
        var c = Create(workspace, s_c);
        var graph = (((GpuBigIntExpr)a + b) + c).CompileToGraph(workspace);
        var compiled = workspace.GetMemoryStatistics();

        workspace.Dispose();
        var afterWorkspace = workspace.GetMemoryStatistics();
        graph.Dispose();
        var afterGraph = workspace.GetMemoryStatistics();

        Assert.True(compiled.DeferredBuffers > 0, $"deferred {compiled.DeferredBuffers}");
        Assert.Equal((0, 0, 0), (afterWorkspace.DeferredBuffers, afterWorkspace.LiveBuffers, afterWorkspace.PooledBuffers));
        Assert.Equal(afterWorkspace, afterGraph);
        Assert.Equal(afterWorkspace.DriverAllocations, afterWorkspace.DriverFrees);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CompileToGraph_TakesTheExpression()
    {
        // The graph owns the expression it was compiled from (G6): evaluating or compiling it again while the graph is
        // alive is an error, because the graph replays the expression's buffers.
        // mutant: CompileToGraph without registering the root node with the graph
        var expression = (GpuBigIntExpr)_a + _b;
        using var graph = expression.CompileToGraph(_workspace);

        Assert.Throws<InvalidOperationException>(() => expression.Evaluate(_workspace));
        Assert.Throws<InvalidOperationException>(() => expression.CompileToGraph(_workspace));
        Assert.Throws<InvalidOperationException>(() => expression.EvaluateWithCudaGraph(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Execute_AfterAnOperandIsFreed_Throws()
    {
        // The graph records raw pointers; a launch after an operand or the target was freed would read or write memory
        // that may belong to someone else, so it throws instead (G7).
        // mutant: GraphHandles without the liveness check before the launch
        var left = Create(_workspace, s_a);
        using var graph = ((GpuBigIntExpr)left + _b).CompileToGraph(_workspace);
        left.Dispose();
        Assert.Throws<ObjectDisposedException>(graph.Execute);

        var target = new GpuBigUInt(_workspace, Words);
        using var targetGraph = ((GpuBigIntExpr)_a + _b).CompileToGraph(_workspace, target);
        target.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = targetGraph.ExecuteAsync(); });
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CaptureFailure_ReturnsEverythingItLeased()
    {
        // A modulo reads its result back to the host, which a capture cannot record, so the capture fails after the
        // warm-up succeeded. The failure path ends the capture and gives back every buffer it deferred (G8); the end of
        // an invalidated capture fails as well, so both errors are reported together (O14).
        // mutant: CompileToGraph without ReturnDeferred on the failure path (DeferredBuffers stays above 0)
        // The workspace uses the shared context: kernels launched in an isolated context would leave function handles
        // of a destroyed context behind for the next test.
        var start = _workspace.GetMemoryStatistics();

        var error = Assert.ThrowsAny<Exception>(
            () => ((((GpuBigIntExpr)_a + _b) % _c) + _a).CompileToGraph(_workspace));
        var failed = _workspace.GetMemoryStatistics();

        Assert.True(error is CudaException or AggregateException { InnerExceptions: [CudaException, ..] },
            error.ToString());
        Assert.Equal(start.DeferredBuffers, failed.DeferredBuffers);

        // The stream is usable again after the failed capture
        using var sum = ((GpuBigIntExpr)_a + _b).Evaluate(_workspace);
        Assert.Equal(s_a + s_b, Read(sum));
    }

    private static GpuBigInt Create(GpuWorkspace workspace, BigInteger value) =>
        new(GpuBigUInt.FromBigInteger(workspace, value, Words), false);

    private static BigInteger Read(GpuBigIntView value) => value.Value.ToBigInteger(value.IsNegative);

    // A view cannot be captured by a lambda, so Assert.Throws cannot read it
    private static bool ResultThrowsAfterDispose(GpuBigIntExecutableGraph graph)
    {
        try
        {
            _ = graph.Result.Value.DevicePointer;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
