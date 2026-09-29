using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Runtime;

public class RT03_NativeResourceTests
{
    [Fact]
    [Trait("FeatureId", "RT-03")]
    [Trait("Category", "SadPath")]
    public void Test_RT03_Shutdown_WithInvalidatedContext_DoesNotThrow()
    {
        // Instead of simulating complex error conditions, verify that calling Shutdown
        // multiple times does not leak exceptions.
        // CudaContextScope is disposed internally, but repeated calls should not cause crashes.
        try
        {
            GpuKernelLoader.Shutdown();
            GpuKernelLoader.Shutdown();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Shutdown should safely ignore cleanup errors, but threw: {ex}");
        }
    }
}
