using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
namespace Kassyi.GSharp.Stdlib.Math.Tests;

[Trait("Category", "GpuExecution")]
public class GBI30_GpuContextTests
{
    [Fact]
    [Trait("FeatureId", "GBI-30")]
    public void Test_GBI30_Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            context.Dispose();
            context.Dispose(); // Multiple calls
        });

        Assert.Null(exception);
    }

    [Fact]
    [Trait("FeatureId", "GBI-30")]
    public void Test_GBI30_Finalizer_ShouldNotCrash()
    {
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
        var context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);
        return new WeakReference(context);
    }

    [Fact]
    [Trait("FeatureId", "GBI-30")]
    public void Test_GBI30_Dispose_WhenUsingGSharpJit_ShouldNotClearSharedProviderCaches()
    {
        // Arrange
        var context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);
        var provider = GSharpJitProvider.Instance;
        var carryPoolField = typeof(GSharpJitProvider).GetField(
            "_carryBufferPool",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var carryPool =
            (ConcurrentDictionary<int,
                Lazy<ConcurrentBag<IntPtr>>>)carryPoolField!.GetValue(provider)!;

        // Add a test-specific unique key
        const int testKey = -99999;
        carryPool.TryAdd(testKey, new Lazy<ConcurrentBag<IntPtr>>(() => [new IntPtr(0x1234)]));

        // Act
        context.Dispose();

        // Assert - Shared provider caches must not be cleared on individual context disposal
        Assert.True(carryPool.ContainsKey(testKey));

        // Cleanup test key only
        carryPool.TryRemove(testKey, out _);
    }

    [Fact]
    [Trait("FeatureId", "GBI-30")]
    public void Test_GBI30_ParallelContextDisposal_ShouldNotCorruptCaches()
    {
        Parallel.For(0, 8, i =>
        {
            using var scope = new JitEngine.CudaContextScope();
            using var ctx = new GpuContext(256, GpuExecutionEngine.GSharpJit);
            using var temp1 = ctx.RentTemp(16);
            using var temp2 = ctx.RentTemp(32);
        });
    }
}
