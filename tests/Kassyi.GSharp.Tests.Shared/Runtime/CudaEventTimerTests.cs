using System;
using System.Threading;
using Xunit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Runtime;

public class CudaEventTimerTests : TranspilerTestBase
{
    public CudaEventTimerTests()
    {
        SkipIfCpuDialect("This test interacts directly with CUDA/Runtime components and cannot run on CPU.");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "HappyPath")]
    public void EventTimer_Lifecycle_And_Synchronization_Works()
    {
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();
        using var timer = new CudaEventTimer();
        
        timer.Start();
        // Just sleep a little to ensure non-zero elapsed time.
        Thread.Sleep(50);
        timer.Stop();
        
        timer.Synchronize();
        
        var ms = timer.GetElapsedTimeMilliseconds();
        Assert.True(ms > 0, "Elapsed time should be greater than zero.");
    }

    [SkippableFact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "SadPath")]
    public void EventTimer_Throws_ObjectDisposedException_When_Used_After_Dispose()
    {
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();
        var timer = new CudaEventTimer();
        timer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => timer.Start());
        Assert.Throws<ObjectDisposedException>(() => timer.Stop());
        Assert.Throws<ObjectDisposedException>(() => timer.Synchronize());
        Assert.Throws<ObjectDisposedException>(() => timer.GetElapsedTimeMilliseconds());
        
        // Repeated disposal should be safe
        var exception = Record.Exception(() => timer.Dispose());
        Assert.Null(exception);
    }
}
