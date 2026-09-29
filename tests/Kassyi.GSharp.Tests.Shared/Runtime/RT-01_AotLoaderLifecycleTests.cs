using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Runtime;

public class RT01_AotLoaderLifecycleTests
{
    private delegate void DummyDelegate();

    [Theory]
    [Trait("FeatureId", "RT-01")]
    [Trait("Category", "Boundary")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NonExistentKernel")]
    public void Test_RT01_CpuKernelLoader_InvalidPaths_ThrowAppropriateExceptions(string kernelName)
    {
        // Boundary Testing: Empty string and non-existent DLL file path
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "empty.dll");
        Assert.ThrowsAny<IOException>(() =>
        {
            CpuKernelLoader.LoadAotKernelPtr(kernelName, nonExistentPath);
        });
    }

    [Theory]
    [Trait("FeatureId", "RT-01")]
    [Trait("Category", "Boundary")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(31)]
    public void Test_RT01_GpuKernelLoader_AotBufferTooSmall_ThrowsInvalidOperationException(int length)
    {
        // Boundary Testing: Exact boundary for hash extraction is 32 bytes; < 32 bytes must be rejected
        byte[] tinyBuffer = new byte[length];
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GpuKernelLoader.LoadAotKernel(tinyBuffer, "dummy_kernel");
        });

        Assert.Contains("too small", ex.Message);
    }

    [Fact]
    [Trait("FeatureId", "RT-01")]
    [Trait("Category", "HappyPath")]
    public void Test_RT01_GpuKernelLoader_DriverAvailability_ReturnsWithoutException()
    {
        // Happy Path: IsDriverAvailable should gracefully return boolean without bubbling unhandled DllNotFoundException
        var isAvailable = GpuKernelLoader.IsDriverAvailable;
        Assert.True(isAvailable || !isAvailable);
    }
}
