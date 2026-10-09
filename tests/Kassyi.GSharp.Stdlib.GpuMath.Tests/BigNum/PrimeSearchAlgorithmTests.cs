using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

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
