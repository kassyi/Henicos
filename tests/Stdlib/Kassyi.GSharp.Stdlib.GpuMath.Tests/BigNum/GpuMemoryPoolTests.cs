namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Release and reuse of pooled device buffers, observed through <see cref="GpuBigUInt.DevicePointer"/>.
/// </summary>
/// <remarks>
/// The pool of <c>DefaultGpuMemoryManager</c> keeps freed buffers per word count in a <c>ConcurrentBag</c>, which hands
/// the buffers added by the same thread back last in, first out. Each test uses its own workspace and a word count
/// (37, 41) that no other allocation in the test uses, so a freed buffer is the next one handed out for that count.
/// </remarks>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuMemoryPoolTests : IDisposable
{
    private const int PoolWords = 37;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuMemoryPoolTests()
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
    public void Dispose_ReturnsBufferToPool_RepeatedReuse()
    {
        // A disposed value goes back to the pool and is handed out again, every time (not only the first time).
        // mutant: DefaultGpuMemoryManager.Free `bag.Add(handle.Buffer)` removed
        // mutant: GpuBigUInt(GpuWorkspace, int) `IsTemp = false` -> `IsTemp = true`
        // mutant: GpuBigUInt.Dispose `MemoryManager.Free(Handle)` removed
        var first = new GpuBigUInt(_workspace, PoolWords);
        var pointer = first.DevicePointer;
        first.Dispose();
        for (var round = 0; round < 3; round++)
        {
            var again = new GpuBigUInt(_workspace, PoolWords);
            Assert.Equal((round, pointer), (round, again.DevicePointer));
            again.Dispose();
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void UIntArrayDispose_ReturnsBufferToPool()
    {
        // GpuBigUIntArray.Dispose frees its buffer (Length * WordsPerElement words) into the pool.
        // mutant: GpuBigUIntArray.Dispose `Workspace.MemoryManager.Free(Handle)` removed
        var array = new GpuBigUIntArray(_workspace, PoolWords, 1);
        var pointer = array.DevicePointer;
        array.Dispose();
        for (var round = 0; round < 3; round++)
        {
            var again = new GpuBigUIntArray(_workspace, PoolWords, 1);
            Assert.Equal((round, pointer), (round, again.DevicePointer));
            again.Dispose();
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void IntArrayDispose_ReturnsValuesAndSignsToPool()
    {
        // GpuBigIntArray.Dispose frees both its values and its signs. With one word per element both are 37 words,
        // so the next two 37-word allocations get exactly those two buffers.
        // mutant: GpuBigIntArray.Dispose `Values.Dispose()` removed
        // mutant: GpuBigIntArray.Dispose `Signs.Dispose()` removed
        var array = new GpuBigIntArray(_workspace, PoolWords, 1);
        IntPtr[] freed = [array.Values.DevicePointer, array.Signs.DevicePointer];
        array.Dispose();

        using var x = new GpuBigUInt(_workspace, PoolWords);
        using var y = new GpuBigUInt(_workspace, PoolWords);
        Assert.Equal(freed.Order(), new[] { x.DevicePointer, y.DevicePointer }.Order());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ReturnTemp_ReturnsBufferToPool()
    {
        // ReturnTemp of a rented buffer puts it back into the pool, and RentTemp hands it out again.
        // mutant: GpuWorkspace.ReturnTemp `MemoryManager.Free(temp.Handle)` removed
        var temp = _workspace.RentTemp(PoolWords);
        Assert.True(temp.IsTemp);
        var pointer = temp.DevicePointer;
        _workspace.ReturnTemp(temp);
        for (var round = 0; round < 3; round++)
        {
            var again = _workspace.RentTemp(PoolWords);
            Assert.Equal((round, pointer), (round, again.DevicePointer));
            _workspace.ReturnTemp(again);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_TwiceOnCopies_DoesNotPoolTwice()
    {
        // GpuBigUInt is a value type: disposing two copies of one value must put its buffer into the pool once,
        // or the next two allocations would share one buffer.
        // mutant: DefaultGpuMemoryManager.Free `!handle.Buffer.TryRetire(...)` -> `handle.Buffer.TryRetire(...)`
        // mutant: DefaultGpuMemoryManager.Free the `return` after a lost TryRetire removed
        var a = new GpuBigUInt(_workspace, PoolWords);
        var b = a;
        var pointer = a.DevicePointer;
        a.Dispose();
        b.Dispose();

        using var x = new GpuBigUInt(_workspace, PoolWords);
        using var y = new GpuBigUInt(_workspace, PoolWords);
        Assert.Equal(pointer, x.DevicePointer);
        Assert.NotEqual(x.DevicePointer, y.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void BorrowedView_Dispose_DoesNotReleaseOwner()
    {
        // A borrowed view (IsTemp) does not own its buffer: disposing it keeps the owner's buffer out of the pool.
        // mutant: GpuBigUInt.Dispose `&& !IsTemp` -> `|| !IsTemp`
        using var owner = new GpuBigUInt(_workspace, PoolWords);
        var view = owner.Handle.AsBorrowedBigUInt(_workspace);
        Assert.True(view.IsTemp);
        view.Dispose();

        using var other = new GpuBigUInt(_workspace, PoolWords);
        Assert.NotEqual(owner.DevicePointer, other.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ReturnTemp_NonTemp_DoesNotRelease()
    {
        // ReturnTemp ignores a value that was not rented; the owner keeps its buffer.
        // mutant: GpuWorkspace.ReturnTemp `!temp.IsTemp` -> `temp.IsTemp`
        using var owner = new GpuBigUInt(_workspace, PoolWords);
        Assert.False(owner.IsTemp);
        _workspace.ReturnTemp(owner);

        using var other = new GpuBigUInt(_workspace, PoolWords);
        Assert.NotEqual(owner.DevicePointer, other.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DivideByUInt32_Zero_ThrowsAndReleasesQuotient()
    {
        // The quotient is allocated before the divisor is checked; when the division throws, it goes back to the pool.
        // The dividend is allocated first so that the quotient takes the pooled buffer `pooled`.
        // mutant: GpuBigUInt.DivideByUInt32 `quotient.Dispose()` removed
        using var dividend = new GpuBigUInt(_workspace, new uint[PoolWords]);
        var spare = new GpuBigUInt(_workspace, PoolWords);
        var pooled = spare.DevicePointer;
        spare.Dispose();

        Assert.Throws<DivideByZeroException>(() => dividend.DivideByUInt32(0));

        using var next = new GpuBigUInt(_workspace, PoolWords);
        Assert.Equal(pooled, next.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void FreshAllocation_IsZeroed()
    {
        // A newly allocated value reads as zero even when the driver hands back memory that held other data.
        // FromBigInteger(workspace, 0) relies on this (GpuBigUInt.cs:497-500).
        // mutant: DefaultGpuMemoryManager.Allocate `provider.ClearMemoryAsync(ptr, numWords, stream)` removed
        var provider = _workspace.Provider;
        var reused = 0;
        foreach (var numWords in new[] { 41, 1024, 1 << 20 })
        {
            var dirty = provider.AllocateMemory(numWords);
            var ones = Enumerable.Repeat(uint.MaxValue, numWords).ToArray();
            provider.CopyHostToDevice(dirty, ones, numWords);
            provider.FreeMemory(dirty);

            // The workspace pool holds no buffer of this size, so the manager allocates from the driver.
            using var fresh = new GpuBigUInt(_workspace, numWords);
            if (fresh.DevicePointer != dirty)
            {
                // The driver gave out another address; that address need not hold the pattern.
                continue;
            }

            reused++;
            Assert.Equal(new uint[numWords], fresh.ToHostArray());
        }

        Skip.If(reused == 0, "The driver never handed back the freed address, so the zero fill was not observed.");
    }
}
