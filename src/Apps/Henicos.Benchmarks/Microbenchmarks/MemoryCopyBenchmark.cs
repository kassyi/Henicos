using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Microbenchmarks;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class MemoryCopyBenchmark
{
    private const int N = 10_000_000;
    private const int BlockSize = 256;
    private const uint GridDimX = (N + BlockSize - 1) / BlockSize;

    private float[] _srcCpu = null!;
    private float[] _dstCpu = null!;

    private GpuBuffer<float>? _dSrc;
    private GpuBuffer<float>? _dDst;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _srcCpu = new float[N];
        _dstCpu = new float[N];

        var rand = new Random(42);
        for (var i = 0; i < N; i++)
        {
            _srcCpu[i] = (float)rand.NextDouble();
        }

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dSrc = new GpuBuffer<float>(N);
        _dDst = new GpuBuffer<float>(N);

        _dSrc.CopyToDevice(_srcCpu);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dSrc?.Dispose();
        _dDst?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        for (var i = 0; i < N; i++)
        {
            _dstCpu[i] = _srcCpu[i];
        }
    }

    [Benchmark]
    public void GSharp_GPU()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        MemoryCopyKernelsWrapper.LaunchMemoryCopyKernel_Device(
            _dSrc!.DevicePointer, _dDst!.DevicePointer, N,
            gridDimX: GridDimX, gridDimY: 1, blockDimX: BlockSize, blockDimY: 1);

        timer.Stop();
        timer.Synchronize();

        // 2 * N * sizeof(float) bytes (1 read, 1 write per element)
        var bytes = 2.0 * N * sizeof(float);
        GpuMetricsContext.RecordBytes(bytes);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void Native_CUDA()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchMemoryCopy_Device(
            _dSrc!.DevicePointer, _dDst!.DevicePointer, N,
            gridDimX: GridDimX, blockDimX: BlockSize);

        timer.Stop();
        timer.Synchronize();

        var bytes = 2.0 * N * sizeof(float);
        GpuMetricsContext.RecordBytes(bytes);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
