using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Microbenchmarks;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class MathFmaBenchmark
{
    private const int N = 1_000_000;
    private const int Iterations = 1000;
    private const int BlockSize = 256;
    private const uint GridDimX = (N + BlockSize - 1) / BlockSize;

    private float[] _dataCpu = null!;

    private Kassyi.GSharp.Runtime.GpuBuffer<float>? _dData;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _dataCpu = new float[N];

        var rand = new Random(42);
        for (var i = 0; i < N; i++)
        {
            _dataCpu[i] = (float)rand.NextDouble();
        }

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dData = new Kassyi.GSharp.Runtime.GpuBuffer<float>(N);

        _dData.CopyToDevice(_dataCpu);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dData?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        for (var i = 0; i < N; i++)
        {
            var val = _dataCpu[i];
            var a = val;
            var b = 1.00001f;
            var c = 0.00001f;

            for (var it = 0; it < Iterations; it++)
            {
                a = a * b + c;
            }

            _dataCpu[i] = a;
        }
    }

    [Benchmark]
    public void GSharpGpu()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        MathFmaKernelsWrapper.Launch_MathFmaKernel_Device(
            _dData!.DevicePointer, N, Iterations,
            gridDimX: GridDimX, gridDimY: 1, blockDimX: BlockSize, blockDimY: 1);

        timer.Stop();
        timer.Synchronize();

        // N elements * Iterations loops * 2 FLOPs per loop (1 mul, 1 add)
        var flops = 2.0 * N * Iterations;
        GpuMetricsContext.RecordFlops(flops);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void NativeCuda()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchMathFma_Device(
            _dData!.DevicePointer, N, Iterations,
            gridDimX: GridDimX, blockDimX: BlockSize);

        timer.Stop();
        timer.Synchronize();

        var flops = 2.0 * N * Iterations;
        GpuMetricsContext.RecordFlops(flops);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
