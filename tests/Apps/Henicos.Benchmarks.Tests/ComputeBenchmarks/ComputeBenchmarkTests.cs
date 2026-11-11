namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

[Category(TestCategory.Benchmark)]
[Collection(TestCollections.GlobalStateSequential)]
public class ComputeBenchmarkTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void SgemmBenchmark_ExecutionAndVerification_Succeeds()
    {
        CudaCapability.EnsureGpuAvailable();
        var bench = new SgemmBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        Assert.Null(Record.Exception(bench.Verify));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Sha256Benchmark_ExecutionAndVerification_Succeeds()
    {
        CudaCapability.EnsureGpuAvailable();
        var bench = new Sha256Benchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        Assert.Null(Record.Exception(bench.Verify));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void BlackScholesBenchmark_ExecutionAndVerification_Succeeds()
    {
        CudaCapability.EnsureGpuAvailable();
        var bench = new BlackScholesBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        Assert.Null(Record.Exception(bench.Verify));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void ReductionBenchmark_ExecutionAndVerification_Succeeds()
    {
        CudaCapability.EnsureGpuAvailable();
        var bench = new ReductionBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        Assert.Null(Record.Exception(bench.Verify));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void ComputeBenchmarkRunner_TryRunSuite_All_ReturnsTrue()
    {
        CudaCapability.EnsureGpuAvailable();
        var result = ComputeBenchmarkRunner.TryRunSuite("all", iterations: 2);
        Assert.True(result);
    }

    [Fact]
    public void ComputeBenchmarkRunner_TryRunSuite_Unknown_ReturnsFalse()
    {
        var result = ComputeBenchmarkRunner.TryRunSuite("nonexistent_suite_xyz");
        Assert.False(result);
    }
}
