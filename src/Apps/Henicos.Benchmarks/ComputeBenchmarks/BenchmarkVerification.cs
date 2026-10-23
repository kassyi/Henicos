namespace Henicos.Benchmarks.ComputeBenchmarks;

/// <summary>Element-wise comparison helpers for the <see cref="IComputeBenchmark.Verify"/> implementations.</summary>
internal static class BenchmarkVerification
{
    public static void AssertEqual(string label, float[] expected, float[] actual) =>
        AssertClose(label, expected, actual, 0.0f);

    public static void AssertClose(string label, float[] expected, float[] actual, float tolerance)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (Math.Abs(expected[i] - actual[i]) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{label} mismatch at index {i}: CPU={expected[i]}, {label}={actual[i]}");
            }
        }
    }
}
