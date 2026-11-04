using Perfolizer.Mathematics.SignificanceTesting.Base;
using Perfolizer.Mathematics.SignificanceTesting.MannWhitney;
using Perfolizer.Metrology;
using Pragmastat;

namespace Henicos.Benchmarks.Comparison;

/// <summary>
/// Compares a base and a head run per benchmark and job with the two-sided Mann-Whitney U test of Perfolizer (the test
/// BenchmarkDotNet uses for its own statistical comparisons).
/// </summary>
public static class BenchmarkComparer
{
    /// <summary>
    /// Compares each benchmark that both runs hold. A benchmark is a regression when its head median is more than
    /// <paramref name="thresholdPercent"/> percent slower and the p-value is below <paramref name="pValueThreshold"/>,
    /// and an improvement when it is as much faster under the same p-value.
    /// </summary>
    public static ComparisonReport Compare(
        Dictionary<string, BenchmarkRun> baseRuns,
        Dictionary<string, BenchmarkRun> headRuns,
        double thresholdPercent,
        double pValueThreshold)
    {
        var comparisons = new List<BenchmarkComparison>();
        var onlyInBase = new List<string>();
        var onlyInHead = new List<string>();

        foreach (var (key, baseRun) in baseRuns)
        {
            if (headRuns.TryGetValue(key, out var headRun))
            {
                comparisons.Add(Compare(baseRun, headRun, thresholdPercent, pValueThreshold));
            }
            else
            {
                onlyInBase.Add(key);
            }
        }

        foreach (var key in headRuns.Keys)
        {
            if (!baseRuns.ContainsKey(key))
            {
                onlyInHead.Add(key);
            }
        }

        comparisons.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        onlyInBase.Sort(StringComparer.Ordinal);
        onlyInHead.Sort(StringComparer.Ordinal);
        return new ComparisonReport(comparisons, onlyInBase, onlyInHead);
    }

    /// <summary>The two-sided Mann-Whitney U test of <paramref name="x"/> against <paramref name="y"/>.</summary>
    /// <returns>The smaller U statistic and the two-sided p-value.</returns>
    public static (double U, double PValue) MannWhitney(double[] x, double[] y)
    {
        var result = MannWhitneyTest.Instance.Perform(new Sample(x), new Sample(y), AlternativeHypothesis.TwoSides,
            Threshold.Zero);
        return (Math.Min(result.Ux, result.Uy), result.PValue.Value);
    }

    private static BenchmarkComparison Compare(BenchmarkRun baseRun, BenchmarkRun headRun, double thresholdPercent,
        double pValueThreshold)
    {
        var baseSummary = SampleSummary.Of(baseRun.Samples);
        var headSummary = SampleSummary.Of(headRun.Samples);
        var (u, pValue) = MannWhitney(baseRun.Samples, headRun.Samples);
        var medianDeltaPercent = baseSummary.Median > 0
            ? (headSummary.Median - baseSummary.Median) / baseSummary.Median * 100.0
            : 0.0;

        var status = ComparisonStatus.Unchanged;
        if (pValue < pValueThreshold)
        {
            if (medianDeltaPercent > thresholdPercent)
            {
                status = ComparisonStatus.Regression;
            }
            else if (medianDeltaPercent < -thresholdPercent)
            {
                status = ComparisonStatus.Improvement;
            }
        }

        return new BenchmarkComparison(baseRun.Key, baseRun.Name, baseSummary, headSummary, medianDeltaPercent, u,
            pValue, status);
    }
}
