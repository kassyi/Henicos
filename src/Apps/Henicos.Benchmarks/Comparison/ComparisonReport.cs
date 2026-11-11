namespace Henicos.Benchmarks.Comparison;

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
