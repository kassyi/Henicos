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
