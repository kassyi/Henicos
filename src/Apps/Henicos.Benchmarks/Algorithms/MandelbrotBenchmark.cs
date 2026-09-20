using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Algorithms;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class MandelbrotBenchmark
{
    private const int Width = 1024;
    private const int Height = 1024;
    private const int MaxIter = 1000;

    private const int TileSize = 16;
    private const uint GridDimX = (Width + TileSize - 1) / TileSize;
    private const uint GridDimY = (Height + TileSize - 1) / TileSize;

    private int[] _outputCpu = null!;
    private Kassyi.GSharp.Runtime.GpuBuffer<int>? _dOutput;
    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _outputCpu = new int[Width * Height];
        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dOutput = new Kassyi.GSharp.Runtime.GpuBuffer<int>(Width * Height);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dOutput?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        float xMin = -2.0f;
        float xMax = 1.0f;
        float yMin = -1.5f;
        float yMax = 1.5f;

        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                float x0 = xMin + (xMax - xMin) * col / Width;
                float y0 = yMin + (yMax - yMin) * row / Height;
                float x = 0.0f;
                float y = 0.0f;
                int iter = 0;

                while (x * x + y * y <= 4.0f && iter < MaxIter)
                {
                    float xtemp = x * x - y * y + x0;
                    y = 2.0f * x * y + y0;
                    x = xtemp;
                    iter++;
                }

                _outputCpu[row * Width + col] = iter;
            }
        }
    }

    [Benchmark]
    public void GSharpGpu()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        MandelbrotKernelsWrapper.LaunchMandelbrotKernel_Device(
            _dOutput!.DevicePointer, Width, Height, MaxIter,
            -2.0f, 1.0f, -1.5f, 1.5f,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: TileSize, blockDimY: TileSize);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void NativeCuda()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchMandelbrot_Device(
            _dOutput!.DevicePointer, Width, Height, MaxIter,
            -2.0f, 1.0f, -1.5f, 1.5f,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: TileSize, blockDimY: TileSize);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
