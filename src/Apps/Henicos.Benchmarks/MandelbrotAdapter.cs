using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class MandelbrotAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new MandelbrotBenchmark();
}
