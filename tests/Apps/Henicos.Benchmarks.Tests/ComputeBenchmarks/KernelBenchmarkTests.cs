namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

[Category(TestCategory.Benchmark)]
[Collection(TestCollections.GlobalStateSequential)]
public class KernelBenchmarkTests
{
    public static TheoryData<string, Func<IComputeBenchmark>> SmallBenchmarks => new()
    {
        { "Mandelbrot", () => new MandelbrotBenchmark(size: 64, maxIter: 100) },
        { "EmptyKernel", () => new EmptyKernelBenchmark() },
        { "MathFma", () => new MathFmaBenchmark(count: 1024, iterations: 100) },
        { "BranchDivergence", () => new BranchDivergenceBenchmark(count: 1024) },
        { "MemoryCopy", () => new MemoryCopyBenchmark(count: 4096) },
        { "Transpose", () => new TransposeBenchmark(size: 64) }
    };

    [SkippableTheory]
    [MemberData(nameof(SmallBenchmarks))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SmallRun_GSharpAndNativeMatchCpuReference(string name, Func<IComputeBenchmark> create)
    {
        CudaCapability.EnsureGpuAvailable();
        var bench = create();
        Assert.False(string.IsNullOrEmpty(name));
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        bench.Verify();
    }

    [Theory]
    [InlineData("mandelbrot", 1)]
    [InlineData("micro", 5)]
    [InlineData("all", 10)]
    [Scenario(TestScenario.Boundary)]
    public void GetBenchmarksForSuite_ReturnsRegisteredBenchmarks(string suite, int expectedCount)
    {
        var benchmarks = ComputeBenchmarkRunner.GetBenchmarksForSuite(suite);
        Assert.Equal(expectedCount, benchmarks.Count);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Verify_DetectsMismatchBetweenBackends()
    {
        CudaCapability.EnsureGpuAvailable();
        var bench = new MemoryCopyBenchmark(count: 1024);
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();

        // The G# output was never produced, so it still holds zeros.
        Assert.Throws<InvalidOperationException>(bench.Verify);
    }
}
