namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed record BenchmarkResult(
    string Name,
    double CpuMeanMs,
    BenchmarkStats NativeStats,
    BenchmarkStats GsharpStats,
    bool Success,
    string ErrorMessage = "");
