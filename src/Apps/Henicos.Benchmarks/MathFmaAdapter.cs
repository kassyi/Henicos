using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class MathFmaAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new MathFmaBenchmark();
}
