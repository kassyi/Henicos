
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.CpuExecution)]
public class PrimeSearchAlgorithmTests
{
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PrimeSearch()
    {
        var prime = PrimeSearchAlgorithm.Generate(64);
        Assert.True(prime > 1);
    }
}
