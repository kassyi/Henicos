using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class EmptyKernelAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new EmptyKernelBenchmark();
}
