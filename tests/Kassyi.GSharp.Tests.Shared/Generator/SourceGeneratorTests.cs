
namespace Kassyi.GSharp.Tests;

public class SourceGeneratorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void TestGeneratedWrapper()
    {
        int[] aArray = [10];
        int[] bArray = [20];
        int[] cArray = [0];

        SourceGeneratorTestKernels.LaunchMyTestKernel(aArray, bArray, cArray);

        Assert.Equal(30, cArray[0]);
    }

}



