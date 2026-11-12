namespace Henicos.Benchmarks.Comparison;

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
