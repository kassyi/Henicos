namespace Henicos.Benchmarks.Comparison;

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
