using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntContextTests
{
    [SkippableFact]
    public void Test_Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        CudaCapability.EnsureGpuAvailable();
        // Arrange
        var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            context.Dispose();
            context.Dispose(); // Multiple calls
        });

        Assert.Null(exception);
    }

    [SkippableFact]
    public void Test_Finalizer_ShouldNotCrash()
    {
        CudaCapability.EnsureGpuAvailable();
        // Arrange & Act
        WeakReference weakRef = CreateAndDropContext();

        // Force Garbage Collection and Finalizers
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Assert.False(weakRef.IsAlive, "Context should have been collected.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference CreateAndDropContext()
    {
        var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        return new WeakReference(context);
    }

    [SkippableFact]
    public void Test_Dispose_WhenUsingGSharpAot_ShouldNotClearSharedProviderCaches()
    {
        CudaCapability.EnsureGpuAvailable();
        // Arrange
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var provider = GSharpKernelProvider.Instance;
        
        // Populate cache by performing an NTT multiplication
        using var a = new GpuBigUInt(context, new uint[] { 1, 2, 3 });
        using var b = new GpuBigUInt(context, new uint[] { 4, 5, 6 });
        using var c = new GpuBigUInt(context, new uint[6]);
        provider.Mul(context, a, b, c);

        var advancedMathField = typeof(GSharpKernelProvider).GetField(
            "_advancedMath", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(advancedMathField);
        var advancedMath = advancedMathField.GetValue(provider);
        Assert.NotNull(advancedMath);

        var twiddleCacheField = advancedMath.GetType().GetField(
            "_twiddleCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(twiddleCacheField);
        var twiddleCache = Assert.IsAssignableFrom<IDictionary>(twiddleCacheField.GetValue(advancedMath));

        Assert.True(twiddleCache.Count > 0, "Cache should be populated after multiplication.");
        int cachedCountBefore = twiddleCache.Count;

        // Act
        context.Dispose();

        // Assert - Shared provider caches must not be cleared on individual context disposal
        Assert.Equal(cachedCountBefore, twiddleCache.Count);
    }

    [SkippableFact]
    public void Test_ParallelContextDisposal_ShouldNotCorruptCaches()
    {
        CudaCapability.EnsureGpuAvailable();
        Parallel.For(0, 8, i =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
            using var temp1 = ctx.RentTemp(16);
            using var temp2 = ctx.RentTemp(32);
        });
    }

    [SkippableFact]
    public void Test_Dispose_WhenTrackedResourceThrows_ShouldThrowAggregateException()
    {
        CudaCapability.EnsureGpuAvailable();
        // Arrange
        var context = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        var expectedEx1 = new InvalidOperationException("Resource 1 failed to dispose.");
        var expectedEx2 = new InvalidOperationException("Resource 2 failed to dispose.");

        context.Track(new ThrowingDisposable(expectedEx1));
        context.Track(new ThrowingDisposable(expectedEx2));

        // Act & Assert
        var aggEx = Assert.Throws<AggregateException>(() => context.Dispose());
        Assert.Contains(expectedEx1, aggEx.InnerExceptions);
        Assert.Contains(expectedEx2, aggEx.InnerExceptions);
    }

    // ---- Workspace lifetime (#598 stage 2b) ----

    private const long MiB = 1024 * 1024;

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void CpuFallback_DeviceSmCountIsOne()
    {
        // Under the CPU fallback no stream is created and the workspace reports one multiprocessor.
        // mutant: GpuWorkspace.cs:155 `DeviceSmCount = 1` removed
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            var workspace = new GpuWorkspace(4);
            Assert.Equal((1, IntPtr.Zero), (workspace.DeviceSmCount, workspace.Stream));
            workspace.Dispose();
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceSmCount_MatchesDriverAttribute()
    {
        // The workspace reports the multiprocessor count of the device (the fallback 16 only when the query fails).
        // On a GPU with exactly 16 multiprocessors this check cannot tell the two apart.
        // mutant: GpuWorkspace.cs:173 `smCount > 0 ? smCount : 16` -> `false ? smCount : 16` / `smCount < 0`
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(
            CudaDriverApi.cuDeviceGetAttribute(out var smCount, CudaDriverApi.CuDeviceAttribute.MultiprocessorCount, device)
        );

        using var workspace = new GpuWorkspace(4);
        Assert.Equal(smCount, workspace.DeviceSmCount);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void DisposeUnmanaged_DoesNotThrowFromTrackedResources()
    {
        // Dispose(false) runs on the finalizer thread, so it must not throw even when a tracked resource does.
        // mutant: GpuWorkspace.cs:229 `disposing ? [] : null` -> `[]` (the errors are collected and thrown)
        CudaCapability.EnsureGpuAvailable();
        var workspace = new UnmanagedDisposeWorkspace();
        workspace.Track(new ThrowingDisposable(new InvalidOperationException("tracked resource failed")));

        var error = Record.Exception(workspace.DisposeUnmanaged);

        Assert.Null(error);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Finalizer_CallsDisposeWithFalse()
    {
        // The finalizer releases the workspace through Dispose(false).
        // mutant: GpuWorkspace.cs:376 `Dispose(false)` -> `Dispose(true)` / call removed
        CudaCapability.EnsureGpuAvailable();
        FinalizerProbeWorkspace.Reset();
        CreateAndDropProbe();

        for (var attempt = 0; attempt < 10 && FinalizerProbeWorkspace.DisposingArgument == FinalizerProbeWorkspace.NotCalled; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(FinalizerProbeWorkspace.CalledWithFalse, FinalizerProbeWorkspace.DisposingArgument);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDropProbe() => _ = new FinalizerProbeWorkspace();

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void IsolatedContext_Dispose_ReleasesContextMemory()
    {
        // A workspace with its own context destroys that context (and its stream) on Dispose.
        // One context takes some hundred MiB, so three leaked contexts stand out.
        // mutant: GpuWorkspace.cs:263 `DisposeEngineResources(exceptions)` removed
        // mutant: GpuWorkspace.cs:351 `Stream == IntPtr.Zero` -> `Stream != IntPtr.Zero`
        // mutant: GpuWorkspace.cs:362 `GpuKernelLoader.DestroyContext(_isolatedContext)` removed
        CudaCapability.EnsureGpuAvailable();
        AssertReleasesDeviceMemory(128 * MiB, static () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var workspace = new GpuWorkspace(4, useIsolatedContext: true);
                workspace.Dispose();
            }
        });
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void IsolatedContext_DisposeTwice_DoesNotThrow()
    {
        // The second Dispose returns at once. Running the teardown again would destroy the already destroyed context.
        // The constructor leaves the new context current on the constructing thread, so the workspace lives on a
        // thread of its own and the test thread keeps a valid current context.
        // mutant: GpuWorkspace.cs:213 `return` removed
        CudaCapability.EnsureGpuAvailable();
        Exception error = null;
        var thread = new Thread(() => error = Record.Exception(() =>
        {
            var workspace = new GpuWorkspace(4, useIsolatedContext: true);
            workspace.Dispose();
            workspace.Dispose();
        }));
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Dispose_ManagerFailure_SurfacesAsAggregate()
    {
        // A failure while the memory manager frees its buffers is reported by Dispose, not dropped.
        // The buffer is freed behind the manager's back, so the manager's own free fails.
        // mutant: GpuWorkspace.cs:345 `exceptions?.Add(ex)` removed
        CudaCapability.EnsureGpuAvailable();
        var workspace = new GpuWorkspace(4);
        using (new GpuKernelLoader.CudaContextScope())
        {
            var value = new GpuBigUInt(workspace, 1024);
            workspace.Provider.FreeMemory(value.DevicePointer);
        }

        Assert.Throws<AggregateException>(workspace.Dispose);
    }

    /// <summary>
    /// Runs <paramref name="action"/> and checks that the free device memory comes back to within
    /// <paramref name="tolerance"/> bytes. Other processes share the GPU, so a disturbed measurement is retried;
    /// a real leak fails every attempt.
    /// </summary>
    private static void AssertReleasesDeviceMemory(long tolerance, Action action)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        long lost = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
            CudaException.ThrowIfError(CudaDriverApi.cuMemGetInfo_v2(out var freeBefore, out _));
            action();
            CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
            CudaException.ThrowIfError(CudaDriverApi.cuMemGetInfo_v2(out var freeAfter, out _));
            lost = (long)freeBefore - (long)freeAfter;
            if (lost < tolerance)
            {
                return;
            }
        }

        Assert.Fail($"Device memory not released: {lost / MiB} MiB less free after the workspace was disposed (tolerance {tolerance / MiB} MiB).");
    }

    private sealed class ThrowingDisposable(Exception exceptionToThrow) : IDisposable
    {
        public void Dispose() => throw exceptionToThrow;
    }

    private sealed class UnmanagedDisposeWorkspace() : GpuWorkspace(4)
    {
        public void DisposeUnmanaged() => Dispose(false);
    }

    private sealed class FinalizerProbeWorkspace() : GpuWorkspace(4)
    {
        public const int NotCalled = -1;
        public const int CalledWithFalse = 0;
        public const int CalledWithTrue = 1;

        private static int s_disposingArgument = NotCalled;

        public static int DisposingArgument => Volatile.Read(ref s_disposingArgument);

        public static void Reset() => Volatile.Write(ref s_disposingArgument, NotCalled);

        protected override void Dispose(bool disposing)
        {
            Volatile.Write(ref s_disposingArgument, disposing ? CalledWithTrue : CalledWithFalse);
            base.Dispose(disposing);
        }
    }
}
