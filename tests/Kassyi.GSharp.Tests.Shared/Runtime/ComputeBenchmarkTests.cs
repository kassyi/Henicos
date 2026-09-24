using Henicos.Benchmarks.ComputeBenchmarks;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

[Trait("Category", "Benchmark")]
public class ComputeBenchmarkTests
{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void SgemmBenchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new SgemmBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        bench.Verify();
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Sha256Benchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new Sha256Benchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        bench.Verify();
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void BlackScholesBenchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new BlackScholesBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        bench.Verify();
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void ReductionBenchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new ReductionBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunNativeCuda();
        bench.RunGSharp();
        bench.Verify();
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void ComputeBenchmarkRunner_RunSuite_All_ReturnsTrue()
    {
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
