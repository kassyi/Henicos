namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Ownership of the device buffers of a workspace: the allocation counters of the memory manager.
/// </summary>
/// <remarks>
/// Each test uses its own workspace and a word count (37) that no other allocation in the test uses, so a freed buffer
/// is the next one handed out for that count. The counters are compared as differences from a snapshot taken at the
/// start of the test, so allocations made by the workspace itself do not matter.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBufferOwnershipTests : IDisposable
{
    private const int PoolWords = 37;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBufferOwnershipTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Statistics_CountLiveAndPooledBuffers()
    {
        // Two allocations are live, a disposed one waits in the pool, and the next allocation of the same size takes it
        // from the pool instead of the driver.
        // mutant: DefaultGpuMemoryManager.Free: the LiveBuffers decrement removed (oneLive stays at 2)
        // mutant: DefaultGpuMemoryManager.Allocate: the PooledBuffers decrement on a pool hit removed
        // mutant: DefaultGpuMemoryManager.Allocate: the DriverAllocations increment moved before the pool lookup
        var start = _workspace.GetMemoryStatistics();
        var a = new GpuBigUInt(_workspace, PoolWords);
        var b = new GpuBigUInt(_workspace, PoolWords);
        var twoLive = _workspace.GetMemoryStatistics();
        a.Dispose();
        var oneLive = _workspace.GetMemoryStatistics();
        var c = new GpuBigUInt(_workspace, PoolWords);
        var reused = _workspace.GetMemoryStatistics();
        b.Dispose();
        c.Dispose();
        var end = _workspace.GetMemoryStatistics();

        Assert.Equal(
            [
                (2, 2L * PoolWords, 0, 2L, 0L),
                (1, 1L * PoolWords, 1, 2L, 0L),
                (2, 2L * PoolWords, 0, 2L, 0L),
                (0, 0L, 2, 2L, 0L),
            ],
            new[] { twoLive, oneLive, reused, end }.Select(s => Delta(start, s)).ToArray()
        );
        Assert.True(end.PeakLiveBuffers >= start.LiveBuffers + 2, $"peak {end.PeakLiveBuffers}, start {start.LiveBuffers}");
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Statistics_IgnoreZeroWordAllocationAndDefaultFree()
    {
        // Allocate(0) hands out the default handle without a buffer, and freeing the default handle does nothing; neither
        // is a lease.
        // mutant: DefaultGpuMemoryManager.Allocate: the `numWords == 0` early return moved after the LiveBuffers increment
        // mutant: DefaultGpuMemoryManager.Free: the `IsDefault` early return moved after the LiveBuffers decrement
        var start = _workspace.GetMemoryStatistics();
        var empty = _workspace.MemoryManager.Allocate(0);
        _workspace.MemoryManager.Free(empty);
        _workspace.MemoryManager.Free(default);

        Assert.True(empty.IsDefault);
        Assert.Equal(start, _workspace.GetMemoryStatistics());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Statistics_WorkspaceDispose_ReleasesPooledAndLiveBuffers()
    {
        // Disposing the workspace returns every buffer to the driver: the pooled one and the one still live, so the
        // disposal frees exactly the buffers that were live or pooled, and none is left.
        // mutant: DefaultGpuMemoryManager.Dispose: the DriverFrees increment of the live buffers removed
        // mutant: DefaultGpuMemoryManager.Dispose: the PooledBuffers decrement removed
        // The live buffer has another word count, so that it does not take the pooled one.
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var start = workspace.GetMemoryStatistics();
        var pooled = new GpuBigUInt(workspace, PoolWords);
        pooled.Dispose();
        _ = new GpuBigUInt(workspace, PoolWords + 1);
        var beforeDispose = workspace.GetMemoryStatistics();

        workspace.Dispose();
        var afterDispose = workspace.GetMemoryStatistics();

        Assert.Equal(
            (1, 1),
            (beforeDispose.LiveBuffers - start.LiveBuffers, beforeDispose.PooledBuffers - start.PooledBuffers)
        );
        Assert.Equal(
            (0, 0L, 0, (long)beforeDispose.LiveBuffers + beforeDispose.PooledBuffers),
            (afterDispose.LiveBuffers, afterDispose.LiveWords, afterDispose.PooledBuffers,
                afterDispose.DriverFrees - beforeDispose.DriverFrees)
        );
    }

    // (live, live words, pooled, driver allocations, driver frees) relative to `start`
    private static (int, long, int, long, long) Delta(GpuMemoryStatistics start, GpuMemoryStatistics now) => (
        now.LiveBuffers - start.LiveBuffers,
        now.LiveWords - start.LiveWords,
        now.PooledBuffers - start.PooledBuffers,
        now.DriverAllocations - start.DriverAllocations,
        now.DriverFrees - start.DriverFrees
    );
}
