using Henicos.Benchmarks.ComputeBenchmarks;
using Xunit;

namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

public class BenchmarkStatsTests
{
    [Fact]
    public void Calculate_EmptyOrNullArray_ReturnsDefault()
    {
        Assert.Equal(default, BenchmarkStats.Calculate(Array.Empty<double>()));
        Assert.Equal(default, BenchmarkStats.Calculate(null!));
    }

    [Fact]
    public void Calculate_OddLengthArray_ReturnsCorrectMedian()
    {
        var samples = new double[] { 5.0, 1.0, 3.0, 2.0, 4.0 };
        var stats = BenchmarkStats.Calculate(samples);

        Assert.Equal(1.0, stats.Min);
        Assert.Equal(3.0, stats.Median);
        Assert.Equal(3.0, stats.Mean);
    }

    [Fact]
    public void Calculate_EvenLengthArray_ReturnsCorrectMedian()
    {
        var samples = new double[] { 1.0, 2.0, 3.0, 4.0 };
        var stats = BenchmarkStats.Calculate(samples);

        Assert.Equal(1.0, stats.Min);
        Assert.Equal(2.5, stats.Median);
        Assert.Equal(2.5, stats.Mean);
    }
}
