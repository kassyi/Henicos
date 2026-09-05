namespace Kassyi.GSharp.Stdlib.Math.Tests;

public class GpuContextTests
{
    [Fact]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
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
    public void Finalizer_ShouldNotCrash()
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference CreateAndDropContext()
    {
        var context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);
        return new WeakReference(context);
    }

    [Fact]
    public void Dispose_WhenUsingGSharpJit_ShouldNotClearSharedProviderCaches()
    {
        // Arrange
        var context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);
        var provider = Kassyi.GSharp.Stdlib.Math.BigInt.Providers.GSharpJitProvider.Instance;

        // Populate caches via dummy rent/return if possible, or just call ClearCaches explicitly to reset state before test
        provider.ClearCaches();

        var carryPoolField = typeof(Kassyi.GSharp.Stdlib.Math.BigInt.Providers.GSharpJitProvider).GetField(
            "_carryBufferPool",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var carryPool =
            (System.Collections.Concurrent.ConcurrentDictionary<int,
                System.Collections.Concurrent.ConcurrentBag<IntPtr>>)carryPoolField!.GetValue(provider)!;

        // Add dummy entry
        carryPool.TryAdd(8, [IntPtr.Zero]);

        // Act
        context.Dispose();

        // Assert - Shared provider caches must not be cleared on individual context disposal
        Assert.NotEmpty(carryPool);

        // Cleanup dummy entry
        carryPool.Clear();
    }
}
