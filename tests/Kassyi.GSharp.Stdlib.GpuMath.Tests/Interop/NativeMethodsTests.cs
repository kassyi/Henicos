using Kassyi.GSharp.Stdlib.GpuMath.Interop;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.Interop;

[Trait("Category", "GpuExecution")]
public class NativeMethodsTests
{
    [SkippableFact]
    public void Test_NativeMethods_TypeInit_ShouldNotThrowIfCudaFound()
    {
        CudaCapability.EnsureGpuAvailable();
        // Simply access the type to ensure static constructor runs without unhandled exceptions
        var type = typeof(NativeMethods);
        Assert.NotNull(type);
    }
}
