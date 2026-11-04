namespace Henicos.Benchmarks.Comparison;

/// <summary>The verdict on one benchmark.</summary>
public enum ComparisonStatus
{
    /// <summary>No significant change beyond the threshold.</summary>
    Unchanged,

    /// <summary>The head median is slower by more than the threshold, and the difference is significant.</summary>
    Regression,

    /// <summary>The head median is faster by more than the threshold, and the difference is significant.</summary>
    Improvement
}

/// <summary>Summary statistics of the samples of one side, in nanoseconds per operation.</summary>
public readonly record struct SampleSummary(int Count, double Median, double Mean, double Min, double Max)
{
    /// <summary>Computes the summary of <paramref name="samples"/>, which must not be empty.</summary>
    public static SampleSummary Of(double[] samples)
    {
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        var sum = 0.0;
        foreach (var sample in sorted)
        {
            sum += sample;
        }

        return new SampleSummary(sorted.Length, median, sum / sorted.Length, sorted[0], sorted[^1]);
    }
}

/// <summary>The comparison of one benchmark and job between the base and the head run.</summary>
/// <param name="Key">The benchmark key (<see cref="BenchmarkRun.Key"/>).</param>
/// <param name="Name">The name shown in the table.</param>
/// <param name="Base">The base samples.</param>
/// <param name="Head">The head samples.</param>
/// <param name="MedianDeltaPercent">How much slower the head median is than the base median, in percent.</param>
/// <param name="U">The smaller of the two Mann-Whitney U statistics.</param>
/// <param name="PValue">The two-sided p-value of the Mann-Whitney U test.</param>
/// <param name="Status">The verdict.</param>
public sealed record BenchmarkComparison(
    string Key,
    string Name,
    SampleSummary Base,
    SampleSummary Head,
    double MedianDeltaPercent,
    double U,
    double PValue,
    ComparisonStatus Status);

/// <summary>The comparisons of the benchmarks on both sides, and the keys found on one side only.</summary>
public sealed record ComparisonReport(
    List<BenchmarkComparison> Comparisons,
    List<string> OnlyInBase,
    List<string> OnlyInHead)
{
    /// <summary>The number of comparisons with <paramref name="status"/>.</summary>
    public int Count(ComparisonStatus status) => Comparisons
        .AsStackEnumerable()
        .Count(status, static (comparison, wanted) => comparison.Status == wanted);
}
