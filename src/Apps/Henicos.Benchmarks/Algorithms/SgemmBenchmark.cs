using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Algorithms;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class SgemmBenchmark
{
    private const int N = 512;
    private const int TileSize = 16;

    private const uint GridDimX = (N + TileSize - 1) / TileSize;
    private const uint GridDimY = (N + TileSize - 1) / TileSize;
    private const uint BlockDimX = TileSize;
    private const uint BlockDimY = TileSize;

    private float[] _a = null!;
    private float[] _b = null!;
    private float[] _cCpu = null!;

    private GpuBuffer<float>? _dA;
    private GpuBuffer<float>? _dB;
    private GpuBuffer<float>? _dC;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _a = new float[N * N];
        _b = new float[N * N];
        _cCpu = new float[N * N];

        var rand = new Random(42);
        for (var i = 0; i < N * N; i++)
        {
            _a[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
            _b[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
        }

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dA = new GpuBuffer<float>(N * N);
        _dB = new GpuBuffer<float>(N * N);
        _dC = new GpuBuffer<float>(N * N);

        _dA.CopyToDevice(_a);
        _dB.CopyToDevice(_b);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dA?.Dispose();
        _dB?.Dispose();
        _dC?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        for (var row = 0; row < N; row++)
        {
            for (var col = 0; col < N; col++)
            {
                var sum = 0.0f;
                for (var k = 0; k < N; k++)
                {
                    sum += _a[row * N + k] * _b[k * N + col];
                }
                _cCpu[row * N + col] = sum;
            }
        }
    }

    [Benchmark]
    public void GSharp_GPU()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        SgemmKernelsWrapper.Launch_SgemmKernel_Device(
            _dA!.DevicePointer, _dB!.DevicePointer, _dC!.DevicePointer, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);

        timer.Stop();
        timer.Synchronize();

        // 2 * N^3 FLOPs for SGEMM
        double flops = 2.0 * N * N * N;
        GpuMetricsContext.RecordFlops(flops);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void Native_CUDA()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchSgemm_Device(
            _dA!.DevicePointer, _dB!.DevicePointer, _dC!.DevicePointer, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);

        timer.Stop();
        timer.Synchronize();

        double flops = 2.0 * N * N * N;
        GpuMetricsContext.RecordFlops(flops);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
