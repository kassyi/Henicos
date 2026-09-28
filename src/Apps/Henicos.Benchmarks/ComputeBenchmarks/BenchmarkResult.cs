namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed record BenchmarkResult(
    string Name,
    double CpuMeanMs,
    BenchmarkStats NativeStats,
    BenchmarkStats GsharpStats,
    bool Success,
    string ErrorMessage = "",
    GpuKernelLoader.KernelResourceAttributes? NativeAttributes = null,
    GpuKernelLoader.KernelResourceAttributes? GsharpAttributes = null,
    int NativeInstructionCount = -1,
    int GsharpInstructionCount = -1);
