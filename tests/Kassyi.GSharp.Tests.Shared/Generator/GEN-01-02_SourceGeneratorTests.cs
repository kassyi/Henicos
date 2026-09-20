
namespace Kassyi.GSharp.Tests;

public class GEN_01_SourceGeneratorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-01")]
    public void TestGeneratedWrapper()
    {
        int[] aArray = [10];
        int[] bArray = [20];
        int[] cArray = [0];

        GEN_01_SourceGeneratorTestKernelsWrapper.LaunchMyTestKernel(aArray, bArray, cArray);

        Assert.Equal(30, cArray[0]);
    }

}



