
namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

[Category(TestCategory.Unit)]
public class ComputeBenchmarkRunnerTests
{
    [Fact]
    public void RunBenchmarkSamples_ExecutesWarmupAndIterations()
    {
        // Arrange
        int executionCount = 0;
        Action benchmarkAction = () => { executionCount++; };
        int iterations = 5;

        // Act
        var stats = ComputeBenchmarkRunner.RunBenchmarkSamples(benchmarkAction, iterations);

        // Assert
        // Warmup (1) + iterations (5) = 6 executions
        Assert.Equal(6, executionCount);
        
        // Since the action is instantaneous, stats should be very small (near 0) but valid
        Assert.True(stats.Mean >= 0);
        Assert.True(stats.Median >= 0);
        Assert.True(stats.Min >= 0);
    }

    [Fact]
    public void RunBenchmark_SuccessfulLifecycle_ReturnsSuccessResult()
    {
        var fake = new FakeBenchmark();
        using var sw = new StringWriter();

        var result = ComputeBenchmarkRunner.RunBenchmark(fake, 3, sw);

        Assert.True(fake.Initialized);
        Assert.Equal(1, fake.CpuCount);
        Assert.Equal(4, fake.NativeCount); // 1 warmup + 3 iterations
        Assert.Equal(4, fake.GSharpCount); // 1 warmup + 3 iterations
        Assert.True(result.Success);
        Assert.Equal("FakeBenchmark", result.Name);
        Assert.Empty(result.ErrorMessage);
    }

    [Fact]
    public void RunBenchmark_VerificationFails_ReturnsFailureResult()
    {
        var fake = new FakeBenchmark { ShouldFailVerify = true };
        using var sw = new StringWriter();

        var result = ComputeBenchmarkRunner.RunBenchmark(fake, 2, sw);

        Assert.False(result.Success);
        Assert.Contains("Verification mismatch", result.ErrorMessage);
    }

    [Fact]
    public void RunSuite_UnknownSuite_ReturnsEmptyList()
    {
        using var sw = new StringWriter();
        var results = ComputeBenchmarkRunner.RunSuite("nonexistent", 1, sw);

        Assert.Empty(results);
        Assert.Contains("No benchmarks found", sw.ToString());
    }

    private sealed class FakeBenchmark : IComputeBenchmark
    {
        public string Name => "FakeBenchmark";
        public bool Initialized { get; private set; }
        public int CpuCount { get; private set; }
        public int NativeCount { get; private set; }
        public int GSharpCount { get; private set; }
        public bool ShouldFailVerify { get; set; }

        public void Initialize() => Initialized = true;
        public void RunReferenceCpu() => CpuCount++;
        public void RunNativeCuda() => NativeCount++;
        public void RunGSharp() => GSharpCount++;
        public void Verify()
        {
            if (ShouldFailVerify)
            {
                throw new InvalidOperationException("Verification mismatch");
            }
        }
    }
}
