using Kassyi.ZeroAlloc.Linq;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public readonly record struct BenchmarkStats(double Min, double Median, double Mean)
{
    public static BenchmarkStats Calculate(double[] samples)
    {
        if (samples == null || samples.Length == 0)
        {
            return default;
        }

        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);

        var min = sorted[0];
        var median = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
            : sorted[sorted.Length / 2];

        var mean = sorted.AsStackEnumerable().Average();
        return new BenchmarkStats(min, median, mean);
    }
}
