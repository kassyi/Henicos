using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Microbenchmarks;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class EmptyKernelBenchmark
{
    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup() => _ctxScope = new GpuKernelLoader.CudaContextScope();

    [GlobalCleanup]
    public void Cleanup() => _ctxScope.Dispose();

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        // CPU does nothing
    }

    [Benchmark]
    public void GSharp_GPU()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        EmptyKernelKernelsWrapper.Launch_EmptyKernel_Device(gridDimX: 1, gridDimY: 1, blockDimX: 1, blockDimY: 1);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void Native_CUDA()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchEmpty_Device(gridDimX: 1, gridDimY: 1, blockDimX: 1, blockDimY: 1);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
