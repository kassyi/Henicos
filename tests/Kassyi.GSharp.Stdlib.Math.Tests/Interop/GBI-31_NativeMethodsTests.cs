using Kassyi.GSharp.Stdlib.Math.Interop;

namespace Kassyi.GSharp.Stdlib.Math.Tests.Interop;

public class GBI31_NativeMethodsTests
{
    [Fact]
    [Trait("FeatureId", "GBI-31")]
    public void Test_GBI31_NativeMethods_TypeInit_ShouldNotThrowIfCudaFound()
    {
        // Simply access the type to ensure static constructor runs without unhandled exceptions
        var type = typeof(NativeMethods);
        Assert.NotNull(type);
    }
}
