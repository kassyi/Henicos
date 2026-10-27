namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Ownership of the device buffers of a workspace: the allocation counters of the memory manager and the generation
/// that makes a stale copy of a freed handle harmless.
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

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Dispose_StaleCopyAfterReuse_DoesNotFreeNewOwner()
    {
        // GpuBigUInt is a value type. Disposing a stale copy after the buffer went to a new owner must not free it
        // (the ABA case: the pointer alone cannot tell the two leases apart).
        // mutant: GpuBigIntDeviceBuffer.TryRetire always returns true (the copy frees `b`, which then reads as zero)
        var start = _workspace.GetMemoryStatistics();
        var a = new GpuBigUInt(_workspace, PoolWords);
        var pointer = a.DevicePointer;
        var copy = a;
        a.Dispose();

        using var b = new GpuBigUInt(_workspace, PoolWords);
        Assert.Equal(pointer, b.DevicePointer);
        var pattern = Pattern(PoolWords);
        _workspace.Provider.CopyHostToDevice(b.DevicePointer, pattern, PoolWords, _workspace.Stream);
        copy.Dispose();

        Assert.Equal(pattern, b.ToHostArray());
        Assert.Equal(1, _workspace.GetMemoryStatistics().LiveBuffers - start.LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DevicePointer_OfFreedHandle_Throws()
    {
        // Reading the pointer of a freed value throws, before and after its buffer is handed out again.
        // mutant: GpuBigIntMemoryHandle.DevicePointer without the generation check
        var a = new GpuBigUInt(_workspace, PoolWords);
        var copy = a;
        a.Dispose();
        Assert.Throws<ObjectDisposedException>(() => copy.DevicePointer);

        using var b = new GpuBigUInt(_workspace, PoolWords);
        Assert.Equal(b.DevicePointer, b.Handle.DevicePointer);
        Assert.Throws<ObjectDisposedException>(() => copy.DevicePointer);
        Assert.Throws<ObjectDisposedException>(() => a.ToHostArray());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Dispose_AfterWorkspaceDispose_IsNoOp()
    {
        // The workspace frees every buffer when it is disposed; a value disposed afterwards must not free it again,
        // however often it is disposed, and its pointer can no longer be read.
        // mutant: DefaultGpuMemoryManager.Dispose without retiring the live buffers (the late Dispose frees again)
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var a = new GpuBigUInt(workspace, 8);
        workspace.Dispose();
        var freedByWorkspace = workspace.GetMemoryStatistics().DriverFrees;

        a.Dispose();
        a.Dispose();

        Assert.Equal(freedByWorkspace, workspace.GetMemoryStatistics().DriverFrees);
        Assert.Throws<ObjectDisposedException>(() => a.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Allocate_AfterWorkspaceDispose_Throws()
    {
        // A disposed workspace hands out no memory (the .NET rule for disposed objects), and a value that survived it
        // stays a no-op to dispose: no driver call happens after the workspace released everything.
        // mutant: DefaultGpuMemoryManager.Allocate without the ObjectDisposedException.ThrowIf
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var survivor = new GpuBigUInt(workspace, PoolWords);
        workspace.Dispose();
        var afterDispose = workspace.GetMemoryStatistics();

        Assert.Throws<ObjectDisposedException>(() => new GpuBigUInt(workspace, PoolWords));
        Assert.Throws<ObjectDisposedException>(() => workspace.MemoryManager.Allocate(PoolWords));
        survivor.Dispose();

        Assert.Equal(afterDispose, workspace.GetMemoryStatistics());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ElementAndSlice_OfReusedBuffer_AreUsable()
    {
        // A view of an array whose buffer has been through the pool belongs to the current lease: it can be read and
        // written without a false ObjectDisposedException.
        // mutant: GpuBigUIntArray.GetElement / Slice build the handle with generation 0 instead of the parent's
        var first = new GpuBigUIntArray(_workspace, PoolWords, 1);
        var pointer = first.DevicePointer;
        first.Dispose();

        using var array = new GpuBigUIntArray(_workspace, PoolWords, 1);
        Assert.Equal(pointer, array.DevicePointer);
        var element = array.GetElement(1);
        var slice = array.Slice(2, 3);
        _workspace.Provider.CopyHostToDevice(element.DevicePointer, [11u], 1, _workspace.Stream);
        _workspace.Provider.CopyHostToDevice(slice.DevicePointer, [22u, 33u, 44u], 3, _workspace.Stream);

        var host = new uint[5];
        _workspace.Provider.CopyDeviceToHost(host, array.DevicePointer, host.Length, _workspace.Stream);
        Assert.Equal([0u, 11u, 22u, 33u, 44u], host);
        Assert.Equal([11u], element.ToHostArray());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Equals_IsHandleIdentity_EqualityOperatorComparesValues()
    {
        // Equals and GetHashCode agree on the identity of the buffer (#638); == and != compare the values on the GPU.
        // mutant: GpuBigUInt.Equals compares the values again (a.Equals(b) becomes true)
        // mutant: the generated operator == calls Equals (a == b becomes false)
        using var a = new GpuBigUInt(_workspace, [7u]);
        using var b = new GpuBigUInt(_workspace, [7u]);
        var copy = a;

        Assert.Equal(
            (false, true, false, true, true, 2),
            (a.Equals(b), a == b, a != b, a.Equals(copy), a.GetHashCode() == copy.GetHashCode(),
                new HashSet<GpuBigUInt> { a, b, copy }.Count)
        );
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void IdentityMembers_AfterDispose_DoNotThrow()
    {
        // Equals, GetHashCode and ToString use the handle only, so a freed value can still be compared, hashed and
        // printed (for example by Assert.Equal or a dictionary).
        // mutant: GpuBigUInt.ToString formats DevicePointer
        using var live = new GpuBigUInt(_workspace, [7u]);
        var freed = new GpuBigUInt(_workspace, [7u]);
        var copy = freed;
        freed.Dispose();
        var signed = new GpuBigInt(copy, isNegative: true);

        Assert.Equal(
            (true, false, true, true),
            (copy.Equals(freed), copy.Equals(live), copy.GetHashCode() == freed.GetHashCode(),
                signed.Equals(new GpuBigInt(freed, isNegative: true)))
        );
        Assert.Contains("GpuBigUInt", copy.ToString());
        _ = signed.GetHashCode();
    }

    // Distinct non-zero words, so that a zero fill or a stale read is visible
    private static uint[] Pattern(int numWords) =>
        Enumerable.Range(0, numWords).Select(static i => 0x9E3779B9u * (uint)(i + 1)).ToArray();

    // (live, live words, pooled, driver allocations, driver frees) relative to `start`
    private static (int, long, int, long, long) Delta(GpuMemoryStatistics start, GpuMemoryStatistics now) => (
        now.LiveBuffers - start.LiveBuffers,
        now.LiveWords - start.LiveWords,
        now.PooledBuffers - start.PooledBuffers,
        now.DriverAllocations - start.DriverAllocations,
        now.DriverFrees - start.DriverFrees
    );
}
