using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// Validates that device memory released with a stream is freed in stream order (<c>cuMemFreeAsync</c>) instead of
/// through <c>cuMemFree_v2</c>, which waits for every stream of the device.
/// </summary>
/// <remarks>
/// An unrelated stream is held busy with a <see cref="StreamGate"/>. An operation that returns while the gate is still
/// closed provably did not wait for that stream; one that waited device-wide only returns after the gate opened.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class StreamOrderedFreeTests
{
    private const int NumWords = 1024;

    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void FreeMemory_WithStream_DoesNotWaitForOtherStreams()
    {
        SkipIfNoDevice();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var busy = new CudaStream();
        using var own = new CudaStream();
        var pointer = handler.AllocateMemory(NumWords);

        using var gate = StreamGate.Enqueue(busy.Handle, GateHoldTime);
        handler.FreeMemory(pointer, own.Handle);

        Assert.False(gate.IsReleased, "A stream-ordered free must not wait for work on unrelated streams.");
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void FreeMemory_WithoutStream_KeepsImmediateDeviceWideSemantics()
    {
        SkipIfNoDevice();
        var handler = new AotMemoryHandler();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var busy = new CudaStream();
        var pointer = handler.AllocateMemory(NumWords);

        using var gate = StreamGate.Enqueue(busy.Handle, GateHoldTime);
        handler.FreeMemory(pointer);

        // Callers without a stream (e.g. buffers used on arbitrary non-blocking streams) rely on the implicit
        // device-wide synchronization to never free memory that in-flight work still reads.
        Assert.True(gate.IsReleased, "Freeing without a stream must stay immediate and device-wide.");
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuWorkspace_Dispose_DoesNotWaitForOtherStreams()
    {
        SkipIfNoDevice();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var busy = new CudaStream();

        var workspace = new GpuWorkspace(NumWords);
        var pooled = new GpuBigUInt(workspace, NumWords);
        pooled.Dispose();                                     // returned to the pool, released by the manager
        var active = new GpuBigUInt(workspace, NumWords);     // still owned, released as an active allocation

        using var gate = StreamGate.Enqueue(busy.Handle, GateHoldTime);
        workspace.Dispose();

        Assert.False(gate.IsReleased, "Workspace teardown must not wait for work on streams it does not own.");
        active.Dispose();                                     // after teardown: must be a no-op, not a double free
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void GpuWorkspace_Dispose_WithAbandonedCapture_ReleasesMemoryWithoutError()
    {
        SkipIfNoDevice();

        var workspace = new GpuWorkspace(NumWords);
        var pooled = new GpuBigUInt(workspace, NumWords);
        pooled.Dispose();
        var active = new GpuBigUInt(workspace, NumWords);

        // A stream-ordered free on a capturing stream would turn into a graph node of the discarded graph and leak.
        workspace.Provider.BeginCapture(workspace.Stream);

        var error = Record.Exception(workspace.Dispose);

        Assert.Null(error);
        active.Dispose();

        // The device must still be usable afterwards.
        using var next = new GpuWorkspace(4);
        using var value = new GpuBigUInt(next, 4);
        value.Clear();
        Assert.True(value.IsZero());
    }

    private static void SkipIfNoDevice()
    {
        // The fallback flag is only populated by driver initialization, so it must run before the flag is read.
        GpuKernelLoader.EnsureInitialized();
        Skip.If(GpuKernelLoader.IsCpuFallbackMode, "Requires a CUDA device.");
    }
}
