using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class MemoryCopyAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new MemoryCopyBenchmark();
}
