namespace Henicos.Benchmarks.Comparison;

/// <summary>
/// The measurements of one benchmark and job, read from a BenchmarkDotNet <c>*-report-full.json</c>.
/// </summary>
/// <param name="Key">
/// Namespace, type, method, parameters and job; the same in a base and a head run of the same benchmark and
/// configuration.
/// </param>
/// <param name="Name">The type, method, parameters and job id, as shown in the comparison table.</param>
/// <param name="File">The report the measurements come from.</param>
/// <param name="Samples">The time of one operation in nanoseconds, per actual workload iteration.</param>
public sealed record BenchmarkRun(string Key, string Name, string File, double[] Samples);
