using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// Validates asynchronous CUDA Graph execution (<see cref="GpuBigIntExecutableGraph.ExecuteAsync"/> and
/// <see cref="GpuBigIntExpr.EvaluateWithCudaGraphAsync"/>) and the lifetime rules of executable graphs.
/// </summary>
/// <remarks>
/// Gates are enqueued only after compilation: compiling evaluates the expression with host synchronization, which a
/// closed gate would stall.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class CudaGraphExecutionTests
{
    private const int Words = 8;

    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);
    private static readonly BigInteger Left = BigInteger.Parse("123456789012345678901234567890");
    private static readonly BigInteger Right = BigInteger.Parse("987654321098765432109876543210");

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task ExecuteAsync_Add_ProducesSum()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var graph = operands.Sum.CompileToGraph(workspace);

        await graph.ExecuteAsync().AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(Left + Right, Read(graph.Result));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task ExecuteAsync_ReturnsBeforeGraphCompletes()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var graph = operands.Sum.CompileToGraph(workspace);
        using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));

        var pending = graph.ExecuteAsync().AsTask();
        Assert.False(pending.IsCompleted, "The launch must not wait for the graph while the stream is busy.");

        await pending.WaitAsync(CompletionTimeout);
        Assert.True(gate.IsReleased, "The wait completed before the work queued ahead of the graph.");
        Assert.Equal(Left + Right, Read(graph.Result));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ExecuteAsync_PreCanceledToken_ReturnsCanceled()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var graph = operands.Sum.CompileToGraph(workspace);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pending = graph.ExecuteAsync(cts.Token);

        Assert.True(pending.IsCanceled);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ExecuteAsync_CanceledInFlight_GraphStillCompletes_AndStreamStaysUsable()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var graph = operands.Sum.CompileToGraph(workspace);
        using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));
        using var cts = new CancellationTokenSource();

        var pending = graph.ExecuteAsync(cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(CompletionTimeout));
        Assert.False(gate.IsReleased, "Cancellation must detach the awaiter before the graph runs.");

        // Cancellation only stops waiting: the graph runs to completion, and stream-ordered work observes it.
        await workspace.Provider.SynchronizeStreamAsync(workspace.Stream).AsTask().WaitAsync(CompletionTimeout);
        Assert.Equal(Left + Right, Read(graph.Result));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task Dispose_WhileInFlight_LetsGraphFinish()
    {
        CudaCapability.EnsureGpuAvailable();

        // The result goes to a target the test owns: the graph's own result buffer is released by Dispose.
        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var target = new GpuBigUInt(workspace, Words);
        var graph = operands.Sum.CompileToGraph(workspace, target);
        using var gate = CudaTestEnvironment.InContext(() => StreamGate.Enqueue(workspace.Stream, GateHoldTime));

        var pending = graph.ExecuteAsync().AsTask();
        graph.Dispose();
        Assert.False(gate.IsReleased, "Dispose must not wait for the graph.");

        await pending.WaitAsync(CompletionTimeout);
        Assert.Equal(Left + Right, target.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void DisposedGraph_RejectsExecution_AndDisposeIsIdempotent()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        var graph = operands.Sum.CompileToGraph(workspace);
        var copy = graph;

        graph.Dispose();
        copy.Dispose();
        default(GpuBigIntExecutableGraph).Dispose();

        Assert.Throws<ObjectDisposedException>(copy.Execute);
        Assert.Throws<ObjectDisposedException>(() => { _ = copy.ExecuteAsync(); });
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteAsync_RepeatedLaunch_DoesNotAllocateInSteadyState()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var graph = operands.Sum.CompileToGraph(workspace);
        for (var i = 0; i < 64; i++)
        {
            SpinUntilConsumed(graph.ExecuteAsync());
        }

        // A per-launch allocation shows up in every window; a single window may catch one-time runtime work.
        var allocatedPerWindow = new long[3];
        for (var window = 0; window < allocatedPerWindow.Length; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 256; i++)
            {
                SpinUntilConsumed(graph.ExecuteAsync());
            }

            allocatedPerWindow[window] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocatedPerWindow[window] == 0)
            {
                return;
            }
        }

        Assert.Fail($"Every window of 256 launches allocated (bytes per window): {string.Join(", ", allocatedPerWindow)}.");
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task EvaluateWithCudaGraphAsync_Add_MatchesSynchronousEvaluation()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var expected = operands.Sum.EvaluateWithCudaGraph(workspace);

        using var actual = await operands.Sum.EvaluateWithCudaGraphAsync(workspace).AsTask().WaitAsync(CompletionTimeout);

        Assert.Equal(Read(expected), Read(actual));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task EvaluateWithCudaGraphAsync_PreCanceledToken_Throws()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64);
        using var operands = Operands.Create(workspace);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await operands.Sum.EvaluateWithCudaGraphAsync(workspace, cancellationToken: cts.Token));
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CpuFallback_AsyncExecution_CompletesSynchronously()
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(64);
            using var operands = Operands.Create(workspace);
            using var graph = operands.Sum.CompileToGraph(workspace);

            var execution = graph.ExecuteAsync();
            Assert.True(execution.IsCompletedSuccessfully);
            Assert.Equal(Left + Right, Read(graph.Result));

            var evaluation = operands.Sum.EvaluateWithCudaGraphAsync(workspace);
            Assert.True(evaluation.IsCompletedSuccessfully);
            using var result = await evaluation;
            Assert.Equal(Left + Right, Read(result));
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    private static BigInteger Read(GpuBigIntView value) => value.Value.ToBigInteger(value.IsNegative);

    private static void SpinUntilConsumed(ValueTask pending)
    {
        // Polls without Sleep(1), which would round every launch up to a timer tick.
        var spinner = new SpinWait();
        while (!pending.IsCompleted)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
        }

        pending.GetAwaiter().GetResult();
    }

    /// <summary>Two device operands and their sum expression.</summary>
    private sealed class Operands(GpuBigInt left, GpuBigInt right) : IDisposable
    {
        public GpuBigIntExpr Sum => left + right;

        public static Operands Create(GpuWorkspace workspace) => new(
            new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, Left, Words), false),
            new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, Right, Words), false));

        public void Dispose()
        {
            left.Dispose();
            right.Dispose();
        }
    }
}
