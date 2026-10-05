using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Trait("Category", "GpuExecution")]
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
        var provider = GSharpAotProvider.Instance;
        
        // Populate cache by performing an NTT multiplication
        using var a = new GpuBigUInt(context, new uint[] { 1, 2, 3 });
        using var b = new GpuBigUInt(context, new uint[] { 4, 5, 6 });
        using var c = new GpuBigUInt(context, new uint[6]);
        provider.Mul(context, a, b, c);

        var advancedMathField = typeof(GSharpAotProvider).GetField(
            "_advancedMath", BindingFlags.NonPublic | BindingFlags.Instance);
        var advancedMath = advancedMathField!.GetValue(provider)!;

        var twiddleCacheField = advancedMath.GetType().GetField(
            "_twiddleCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var twiddleCache = (System.Collections.IDictionary)twiddleCacheField!.GetValue(advancedMath)!;

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

    private sealed class ThrowingDisposable(Exception exceptionToThrow) : IDisposable
    {
        public void Dispose() => throw exceptionToThrow;
    }
}
