
namespace Kassyi.GSharp.Tests;

public class GEN_01_SourceGeneratorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-01")]
    public void TestGeneratedWrapper()
    {
        int[] aArray = [10];
        int[] bArray = [20];
        int[] cArray = [0];

        GEN_01_SourceGeneratorTestKernelsWrapper.Launch_MyTestKernel(aArray, bArray, cArray);

        Assert.Equal(30, cArray[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-02")]
    public void TestInvalidKernelCompilation_ThrowsWrappedException()
    {
        var invalidCode = "extern \"C\" __global__ void bad_kernel(int* x) { this is invalid c++ code }";

        var ex = Assert.Throws<Exception>(() =>
            Kassyi.GSharp.Runtime.Jit.JitEngine.GetOrCompileKernel(invalidCode, "bad_kernel")
        );

        Assert.Contains("NVRTC Compilation Failed", ex.Message);
    }
}



