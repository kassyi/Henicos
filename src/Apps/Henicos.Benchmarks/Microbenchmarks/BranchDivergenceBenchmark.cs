using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Microbenchmarks;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class BranchDivergenceBenchmark
{
    private const int N = 1_000_000;
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
            if (i % 2 == 0)
            {
                for (var j = 0; j < 100; j++)
                {
                    val = val * 1.01f + 0.01f;
                }
            }
            else
            {
                for (var j = 0; j < 100; j++)
                {
                    val = val * 0.99f - 0.01f;
                }
            }
            _dataCpu[i] = val;
        }
    }

    [Benchmark]
    public void GSharp_GPU()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        BranchDivergenceKernelsWrapper.LaunchBranchDivergenceKernel_Device(
            _dData!.DevicePointer, N,
            gridDimX: GridDimX, gridDimY: 1, blockDimX: BlockSize, blockDimY: 1);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void Native_CUDA()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchBranchDivergence_Device(
            _dData!.DevicePointer, N,
            gridDimX: GridDimX, blockDimX: BlockSize);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
