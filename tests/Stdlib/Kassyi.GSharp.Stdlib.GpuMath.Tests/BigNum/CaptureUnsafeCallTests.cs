#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// An allocation that misses the pool calls <c>cuMemAlloc</c>, which the driver forbids while the calling thread
/// captures in global or thread-local mode. In the global context it fails with <see cref="CudaException"/>
/// (<see cref="CUresult.CudaErrorStreamCaptureUnsupported"/>) and the process survives; in relaxed mode it allocates.
/// </summary>
/// <remarks>
/// The forbidding modes run in a child process: in a context of <c>cuCtxCreate</c> the driver ends the process with an
/// access violation instead of returning the error (NVIDIA bug 7081224), which would end the test host. An isolated
/// workspace still has such a context, so its test is skipped until a driver returns the error there.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class CaptureUnsafeCallTests
{
    // A new workspace has an empty pool, so a number of this size always calls cuMemAlloc
    private const int Words = 1 << 20;

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void AllocationMissingThePool_DuringThreadLocalCapture_ThrowsCaptureUnsupported()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name = nameof(AllocationMissingThePool_DuringThreadLocalCapture_ThrowsCaptureUnsupported);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(CaptureUnsafeCallTests), Name);
            return;
        }

        AssertAllocationIsRefused(CUstreamCaptureMode.ThreadLocal);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void AllocationMissingThePool_DuringGlobalCapture_ThrowsCaptureUnsupported()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name = nameof(AllocationMissingThePool_DuringGlobalCapture_ThrowsCaptureUnsupported);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(CaptureUnsafeCallTests), Name);
            return;
        }

        AssertAllocationIsRefused(CUstreamCaptureMode.Global);
    }

    [SkippableFact(Skip = "In a context of cuCtxCreate the driver ends the process instead of returning the error " +
        "(NVIDIA bug 7081224, driver 621.14).")]
    [Scenario(TestScenario.SadPath)]
    public void AllocationMissingThePool_InIsolatedContextDuringThreadLocalCapture_ThrowsCaptureUnsupported()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name =
            nameof(AllocationMissingThePool_InIsolatedContextDuringThreadLocalCapture_ThrowsCaptureUnsupported);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(CaptureUnsafeCallTests), Name);
            return;
        }

        AssertAllocationIsRefused(CUstreamCaptureMode.ThreadLocal, useIsolatedContext: true);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void AllocationMissingThePool_DuringRelaxedCapture_Allocates()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        long allocationsBefore = workspace.GetMemoryStatistics().DriverAllocations;

        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(workspace.Stream, CUstreamCaptureMode.Relaxed));
        Exception? thrown;
        try
        {
            thrown = Record.Exception(() => new GpuBigUInt(workspace, Words).Dispose());
        }
        finally
        {
            var end = EndCapture(workspace.Stream);
            Assert.Equal(CUresult.CudaSuccess, end);
        }

        Assert.Null(thrown);
        Assert.Equal(allocationsBefore + 1, workspace.GetMemoryStatistics().DriverAllocations);
    }

    private static void AssertAllocationIsRefused(CUstreamCaptureMode mode, bool useIsolatedContext = false)
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot, useIsolatedContext);
        using var scope = workspace.ActivateContext();

        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(workspace.Stream, mode));
        var thrown = Record.Exception(() => new GpuBigUInt(workspace, Words).Dispose());

        // The refused call invalidates the capture, which must still be ended on this thread
        var end = EndCapture(workspace.Stream);

        var cuda = Assert.IsType<CudaException>(thrown);
        Assert.Equal(CUresult.CudaErrorStreamCaptureUnsupported, cuda.Result);
        Assert.Equal(CUresult.CudaErrorStreamCaptureInvalidated, end);
    }

    private static CUresult EndCapture(IntPtr stream)
    {
        var end = CudaDriverApi.cuStreamEndCapture(stream, out var graph);
        if (graph != IntPtr.Zero)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuGraphDestroy(graph));
        }

        return end;
    }
}
