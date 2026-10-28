#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Disposing a <see cref="GpuWorkspace"/> gives its device memory back to the driver (#659).
/// </summary>
/// <remarks>
/// The allocation counters of the memory manager count the calls that free a buffer, not the memory that comes back:
/// a stream-ordered free on a stream that is destroyed before it runs is counted but never returns the memory. These
/// tests therefore also read the free device memory (<c>cuMemGetInfo_v2</c> after <c>cuCtxSynchronize</c>). Each
/// iteration pools a 256 MiB buffer, so a leak shows as 256 MiB per iteration, far above the 64 MiB tolerance for
/// what the driver and other processes allocate meanwhile. The test assembly runs its tests one at a time
/// (<c>xunit.runner.json</c>), so no other test of this assembly allocates during the measurement.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuWorkspaceReleaseTests : IDisposable
{
    private const int Iterations = 3;

    // 64 Mi words of 4 bytes: 256 MiB per workspace.
    private const int LargeWords = 64 * 1024 * 1024;

    private const long ToleranceBytes = 64L * 1024 * 1024;

    // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL, as KernelGraphHandler uses.
    private const int ThreadLocalCaptureMode = 1;

    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GpuWorkspaceReleaseTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose() => _scope.Dispose();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Dispose_PooledBuffer_ReturnsDeviceMemory()
    {
        // Each workspace returns a 256 MiB buffer to its pool and is then disposed; the pooled buffer goes back to the
        // driver, so the free device memory does not shrink from one workspace to the next.
        // mutant: GpuWorkspace.DisposeEngineResources: the cuStreamSynchronize before cuStreamDestroy removed
        //         (768 MiB missing after three workspaces)
        AssertDisposeReturnsMemory(static workspace =>
        {
            using (new GpuBigUInt(workspace, LargeWords))
            {
            }
        });
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Dispose_LiveBuffer_ReturnsDeviceMemory()
    {
        // A buffer still live when the workspace is disposed goes back to the driver as well.
        // mutant: GpuWorkspace.DisposeEngineResources: the cuStreamSynchronize before cuStreamDestroy removed
        AssertDisposeReturnsMemory(static workspace => _ = new GpuBigUInt(workspace, LargeWords));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Dispose_WithAbandonedCapture_ReturnsDeviceMemory()
    {
        // The stream was left capturing (an evaluation threw inside a capture). Disposal ends the capture first, so the
        // frees are real frees on the stream rather than nodes of the discarded graph, and the memory comes back.
        // mutant: GpuWorkspace.DisposeEngineResources: the cuStreamSynchronize before cuStreamDestroy removed
        AssertDisposeReturnsMemory(static workspace =>
        {
            using (new GpuBigUInt(workspace, LargeWords))
            {
            }

            CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(workspace.Stream, ThreadLocalCaptureMode));
        });
    }

    private static void AssertDisposeReturnsMemory(Action<GpuWorkspace> use)
    {
        // A first workspace outside the measurement loads the kernel modules and anything else created once per
        // process, so that only the memory of the measured workspaces is compared.
        RunWorkspace(use);
        long start = FreeDeviceMemory();

        for (int i = 0; i < Iterations; i++)
        {
            var (beforeUse, afterUse, afterDispose) = RunWorkspace(use);

            // The large buffer is the one buffer the use added, live or pooled.
            Assert.Equal(
                1,
                afterUse.LiveBuffers + afterUse.PooledBuffers - (beforeUse.LiveBuffers + beforeUse.PooledBuffers)
            );
            Assert.Equal(
                (0, 0L, 0, afterDispose.DriverAllocations),
                (afterDispose.LiveBuffers, afterDispose.LiveWords, afterDispose.PooledBuffers,
                    afterDispose.DriverFrees)
            );
        }

        long missing = start - FreeDeviceMemory();
        Assert.True(
            missing <= ToleranceBytes,
            $"{missing / (1024 * 1024)} MiB of device memory did not come back after {Iterations} workspaces were disposed."
        );
    }

    private static (GpuMemoryStatistics BeforeUse, GpuMemoryStatistics AfterUse, GpuMemoryStatistics AfterDispose)
        RunWorkspace(Action<GpuWorkspace> use)
    {
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var beforeUse = workspace.GetMemoryStatistics();
        use(workspace);
        var afterUse = workspace.GetMemoryStatistics();
        workspace.Dispose();
        return (beforeUse, afterUse, workspace.GetMemoryStatistics());
    }

    private static long FreeDeviceMemory()
    {
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
        CudaException.ThrowIfError(CudaDriverApi.cuMemGetInfo_v2(out nint free, out _));
        return free;
    }
}
