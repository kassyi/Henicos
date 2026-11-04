namespace Henicos.Benchmarks.Tests;

[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GeneratorBenchmarkTests
{
    // Setup throws when the generator reports an error or leaves out a wrapper or AOT source of an input class
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Setup_GeneratorProducesEveryExpectedSource_BenchmarksReturnWork()
    {
        var benchmark = new GeneratorBenchmark();

        benchmark.Setup();

        Assert.True(benchmark.RunGenerator_Cuda() > 0);
        Assert.True(benchmark.RunGenerator_Cpu() > 0);
    }
}
