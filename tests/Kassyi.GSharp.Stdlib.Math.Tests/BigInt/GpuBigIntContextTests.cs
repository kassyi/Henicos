using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt.Providers;
namespace Kassyi.GSharp.Stdlib.Math.Tests.GpuBigInt;

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
        using var a = new GpuUIntBigInteger(context, new uint[] { 1, 2, 3 });
        using var b = new GpuUIntBigInteger(context, new uint[] { 4, 5, 6 });
        using var c = new GpuUIntBigInteger(context, new uint[6]);
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
}
