using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class TransposeAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new TransposeBenchmark();
}
