using BenchmarkDotNet.Order;
using Henicos.Benchmarks.ComputeBenchmarks;
using Kassyi.GSharp.Runtime.Interop;

namespace Henicos.Benchmarks;

/// <summary>
/// Runs an <see cref="IComputeBenchmark"/> under BenchmarkDotNet. Each GPU run is wrapped in CUDA events and the
/// result goes to <see cref="GpuMetricsContext"/>, which <see cref="GpuMetricsDiagnoser"/> turns into the kernel time
/// and throughput columns. Benchmarks that update device data in place (and the managed-array launches) include their
/// host-device copies in that time.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public abstract class ComputeBenchmarkAdapter
{
    private IComputeBenchmark _benchmark = null!; // Assigned in Setup before any benchmark method runs

    protected abstract IComputeBenchmark Create();

    [GlobalSetup]
    public void Setup()
    {
        _benchmark = Create();
        _benchmark.Initialize();
    }

    [Benchmark(Baseline = true)]
    public void Cpu() => _benchmark.RunReferenceCpu();

    [Benchmark]
    public void GSharpGpu() => RunTimed(_benchmark.RunGSharp);

    [Benchmark]
    public void NativeCuda() => RunTimed(_benchmark.RunNativeCuda);

    private void RunTimed(Action run)
    {
        using var timer = new CudaEventTimer();
        timer.Start();
        run();
        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
        GpuMetricsContext.RecordFlops(_benchmark.TheoreticalFlops);
        GpuMetricsContext.RecordBytes(_benchmark.TheoreticalBytes);
    }
}

public class EmptyKernelAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new EmptyKernelBenchmark();
}

public class MathFmaAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new MathFmaBenchmark();
}

public class BranchDivergenceAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new BranchDivergenceBenchmark();
}

public class MemoryCopyAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new MemoryCopyBenchmark();
}

public class TransposeAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new TransposeBenchmark();
}

public class MandelbrotAdapter : ComputeBenchmarkAdapter
{
    protected override IComputeBenchmark Create() => new MandelbrotBenchmark();
}
