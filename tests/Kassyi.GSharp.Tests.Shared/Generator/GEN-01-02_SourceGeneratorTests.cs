
namespace Kassyi.GSharp.Tests;

public class GEN_01_SourceGeneratorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-01")]
    public void TestGeneratedWrapper()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        int[] aArray = [10];
        int[] bArray = [20];
        int[] cArray = [0];

        GEN_01_SourceGeneratorTestKernelsWrapper.Launch_MyTestKernel(aArray, bArray, cArray);

        Assert.Equal(30, cArray[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-02")]
    public void TestInvalidKernelCompilation_ThrowsWrappedException()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        var invalidCode = "extern \"C\" __global__ void bad_kernel(int* x) { this is invalid c++ code }";

        var ex = Assert.Throws<Exception>(() =>
            Kassyi.GSharp.Runtime.Jit.JitEngine.GetOrCompileKernel(invalidCode, "bad_kernel")
        );

        Assert.Contains("NVRTC Compilation Failed", ex.Message);
    }
}



