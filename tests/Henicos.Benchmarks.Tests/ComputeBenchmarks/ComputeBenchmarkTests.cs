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
        bench.Verify();
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
        bench.Verify();
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
        bench.Verify();
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
        bench.Verify();
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void ComputeBenchmarkRunner_RunSuite_All_ReturnsTrue()
    {
        CudaCapability.EnsureGpuAvailable();
        var result = ComputeBenchmarkRunner.RunSuite("all", iterations: 2);
        Assert.True(result);
    }

    [Fact]
    public void ComputeBenchmarkRunner_RunSuite_Unknown_ReturnsFalse()
    {
        var result = ComputeBenchmarkRunner.RunSuite("nonexistent_suite_xyz");
        Assert.False(result);
    }
}
