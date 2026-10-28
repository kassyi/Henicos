#nullable enable
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

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void UIntArrayElement_Dispose_KeepsParent()
    {
        // An element is borrowed: disposing it leaves the parent's buffer allocated, so the next allocation of the
        // parent's size gets a new, whole buffer that does not overlap the parent (#630). The public views have no
        // Dispose (GpuMathViewEscapeTests), so this goes through the internal borrowed values of the owned type.
        // mutant: GpuBigUIntArray.GetElement returns an owning handle (Handle.Slice -> an owned whole-buffer handle)
        using var array = new GpuBigUIntArray(_workspace, 4, 2);
        var start = array.DevicePointer;
        var end = start + array.TotalWords * 4;

        array.BorrowElement(1).Dispose();
        array.BorrowSlice(1, 2).Dispose();
        using var next = new GpuBigUInt(_workspace, array.TotalWords);

        Assert.Equal(
            (array.TotalWords, false),
            (next.NumWords, next.DevicePointer >= start && next.DevicePointer < end)
        );
        Assert.Equal(start, array.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void IntArrayElementAndSlice_Dispose_KeepParentValues()
    {
        // Disposing an element or a slice of a GpuBigIntArray keeps the parent's values (#630).
        // mutant: GpuBigUIntArray.Dispose frees a borrowed handle (the OwnsBuffer check removed)
        using var array = new GpuBigIntArray(_workspace, 3, 2);
        uint[] values = [11u, 0u, 22u, 0u, 33u, 0u];
        _workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, values, values.Length, _workspace.Stream);

        array.BorrowSlice(1, 2).Dispose();
        array.BorrowElement(0).Dispose();

        var host = new uint[values.Length];
        _workspace.Provider.CopyDeviceToHost(host, array.Values.DevicePointer, host.Length, _workspace.Stream);
        Assert.Equal(values, host);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FloatArrayElement_Dispose_KeepsParentSignificand()
    {
        // GpuBigFloatArray.GetElement builds its value from a significand element; disposing it frees nothing (#630).
        // mutant: GpuBigUInt.Dispose frees a borrowed handle (the OwnsBuffer check removed)
        using var array = new GpuBigFloatArray(_workspace, 2, 2);
        uint[] significands = [5u, 6u, 7u, 8u];
        _workspace.Provider.CopyHostToDevice(array.Significands.DevicePointer, significands, significands.Length,
            _workspace.Stream);

        array.GetElement(0).AsBorrowed().Dispose();

        var host = new uint[significands.Length];
        _workspace.Provider.CopyDeviceToHost(host, array.Significands.DevicePointer, host.Length, _workspace.Stream);
        Assert.Equal(significands, host);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Free_BorrowedOrPartialHandle_Throws()
    {
        // Only the owning handle of a whole buffer can be freed. A whole-array slice has offset 0 and all words, so only
        // the ownership check rejects it; an element is rejected as well. Neither reaches the pool.
        // mutant: DefaultGpuMemoryManager.Free without the `!handle.OwnsBuffer` check
        using var array = new GpuBigUIntArray(_workspace, PoolWords, 1);
        var whole = array.Slice(0, array.Length).Handle;
        var element = array.GetElement(1).Handle;
        var before = _workspace.GetMemoryStatistics();

        Assert.Equal((0, PoolWords), (whole.WordOffset, whole.NumWords));
        Assert.Throws<InvalidOperationException>(() => _workspace.MemoryManager.Free(whole));
        Assert.Throws<InvalidOperationException>(() => _workspace.MemoryManager.Free(element));
        Assert.Equal(before, _workspace.GetMemoryStatistics());
        Assert.Equal((true, false, false), (array.Handle.OwnsBuffer, whole.OwnsBuffer, element.OwnsBuffer));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void View_AfterOwnerDispose_Throws()
    {
        // A view cannot leave its method, but inside the method its owner can still be disposed first; the generation
        // turns the use after free into ObjectDisposedException (V1).
        // mutant: GpuBigIntMemoryHandle.DevicePointer without the generation check (the view reads freed memory)
        var array = new GpuBigUIntArray(_workspace, PoolWords, 1);
        var element = array.GetElement(0);
        array.Dispose();

        // A view cannot be captured by a lambda (CS8175), so Assert.Throws cannot call it
        ObjectDisposedException? error = null;
        try
        {
            element.ToBigInteger();
        }
        catch (ObjectDisposedException ex)
        {
            error = ex;
        }

        Assert.NotNull(error);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void View_ToOwned_CopiesTheValue()
    {
        // ToOwned copies the element into a buffer of its own, which outlives the array (V2).
        // mutant: GpuBigUIntView.ToOwned wraps the view instead of copying (the value dies with the array)
        var array = new GpuBigUIntArray(_workspace, 3, 2);
        uint[] words = [1u, 2u, 3u, 4u, 5u, 6u];
        _workspace.Provider.CopyHostToDevice(array.DevicePointer, words, words.Length, _workspace.Stream);
        using var owned = array.GetElement(1).ToOwned();
        array.Dispose();

        Assert.Equal((true, 2, new BigInteger(3) + (new BigInteger(4) << 32)),
            (owned.Handle.OwnsBuffer, owned.NumWords, owned.ToBigInteger()));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void View_ToOwned_EmptyAndFailedCopy()
    {
        // ToOwned of the empty view returns default (no workspace to allocate from), and a copy that fails leaves no
        // buffer behind: the view of a freed owner throws, and the new buffer goes back to the pool.
        // mutant: GpuBigUIntView.ToOwned without the IsEmpty check (NullReferenceException on the null workspace)
        // mutant: GpuBigUIntView.ToOwned without the Dispose in its catch (LiveBuffers grows by one)
        Assert.True(default(GpuBigUIntView).ToOwned().Handle.IsDefault);

        var array = new GpuBigUIntArray(_workspace, PoolWords, 1);
        var element = array.GetElement(0);
        array.Dispose();
        var before = _workspace.GetMemoryStatistics();
        ObjectDisposedException? error = null;
        try
        {
            element.ToOwned();
        }
        catch (ObjectDisposedException ex)
        {
            error = ex;
        }

        Assert.NotNull(error);
        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Operation_AcceptsViewsAndOwnedValues()
    {
        // The public operations take views; an element view and an owned value can be mixed, and an element other than
        // the first keeps its offset (V3).
        // mutant: the GpuBigUInt -> GpuBigUIntView conversion drops WordOffset (element 1 reads element 0)
        using var array = new GpuBigUIntArray(_workspace, 2, 2);
        uint[] words = [10u, 0u, 20u, 0u];
        _workspace.Provider.CopyHostToDevice(array.DevicePointer, words, words.Length, _workspace.Stream);
        using var five = GpuBigUInt.FromBigInteger(_workspace, 5, 2);
        using var first = new GpuBigUInt(_workspace, 2);
        using var second = new GpuBigUInt(_workspace, 2);

        GpuBigIntMath.Add(array.GetElement(0), five, first);
        GpuBigIntMath.Add(five, array.GetElement(1), second);

        // The owned value also compares against a view (25 > 20 = element 1)
        Assert.Equal((new BigInteger(15), new BigInteger(25), 1),
            (first.ToBigInteger(), second.ToBigInteger(), Math.Sign(second.CompareTo(array.GetElement(1)))));
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
