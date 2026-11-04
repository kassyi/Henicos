namespace Henicos.Benchmarks.Comparison;

/// <summary>
/// A report that cannot be read or two runs that cannot be compared (a missing path, malformed JSON, a benchmark
/// without measurements, or the same benchmark and job twice on one side).
/// </summary>
public sealed class BenchmarkReportException(string message, Exception? innerException = null)
    : Exception(message, innerException);
