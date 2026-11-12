using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks;

public class BranchDivergenceAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new BranchDivergenceBenchmark();
}
