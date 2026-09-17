#if GSHARP_CPU
using Henicos.PiBenchmark.ComputeBenchmarks;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public class ComputeBenchmarkTests
{
    [Fact]
    public void SgemmBenchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new SgemmBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunGSharpCpu();
        bench.Verify();
    }

    [Fact]
    public void Sha256Benchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new Sha256Benchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunGSharpCpu();
        bench.Verify();
    }

    [Fact]
    public void BlackScholesBenchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new BlackScholesBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunGSharpCpu();
        bench.Verify();
    }

    [Fact]
    public void ReductionBenchmark_ExecutionAndVerification_Succeeds()
    {
        var bench = new ReductionBenchmark();
        bench.Initialize();
        bench.RunReferenceCpu();
        bench.RunGSharpCpu();
        bench.Verify();
    }

    [Fact]
    public void ComputeBenchmarkRunner_RunSuite_All_ReturnsTrue()
    {
        var result = ComputeBenchmarkRunner.RunSuite("all");
        Assert.True(result);
    }

    [Fact]
    public void ComputeBenchmarkRunner_RunSuite_Unknown_ReturnsFalse()
    {
        var result = ComputeBenchmarkRunner.RunSuite("nonexistent_suite_xyz");
        Assert.False(result);
    }
}
#endif
