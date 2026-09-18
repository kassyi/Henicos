using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Microbenchmarks;

[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TransposeBenchmark
{
    private const int N = 2048; // 2048x2048 matrix
    private const int TileSize = 16;

    private const uint GridDimX = (N + TileSize - 1) / TileSize;
    private const uint GridDimY = (N + TileSize - 1) / TileSize;
    private const uint BlockDimX = TileSize;
    private const uint BlockDimY = TileSize;

    private float[] _inputCpu = null!;
    private float[] _outputCpu = null!;

    private Kassyi.GSharp.Runtime.GpuBuffer<float>? _dInput;
    private Kassyi.GSharp.Runtime.GpuBuffer<float>? _dOutput;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _inputCpu = new float[N * N];
        _outputCpu = new float[N * N];

        var rand = new Random(42);
        for (var i = 0; i < N * N; i++)
        {
            _inputCpu[i] = (float)rand.NextDouble();
        }

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dInput = new Kassyi.GSharp.Runtime.GpuBuffer<float>(N * N);
        _dOutput = new Kassyi.GSharp.Runtime.GpuBuffer<float>(N * N);

        _dInput.CopyToDevice(_inputCpu);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dInput?.Dispose();
        _dOutput?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        for (var row = 0; row < N; row++)
        {
            for (var col = 0; col < N; col++)
            {
                _outputCpu[col * N + row] = _inputCpu[row * N + col];
            }
        }
    }

    [Benchmark]
    public void GSharpGpu()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        TransposeKernelsWrapper.Launch_TransposeKernel_Device(
            _dInput!.DevicePointer, _dOutput!.DevicePointer, N, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);

        timer.Stop();
        timer.Synchronize();

        // 2 * N^2 * sizeof(float) bytes (1 read, 1 write per element)
        double bytes = 2.0 * N * N * sizeof(float);
        GpuMetricsContext.RecordBytes(bytes);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void NativeCuda()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchTranspose_Device(
            _dInput!.DevicePointer, _dOutput!.DevicePointer, N, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);

        timer.Stop();
        timer.Synchronize();

        double bytes = 2.0 * N * N * sizeof(float);
        GpuMetricsContext.RecordBytes(bytes);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
