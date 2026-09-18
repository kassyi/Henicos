using BenchmarkDotNet.Attributes;
using Henicos.PiBenchmark.Infrastructure;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.PiBenchmark.ComputeBenchmarks;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class ReductionBenchmark
{
    private const int N = 16 * 1024 * 1024; // 16M elements
    private const int ThreadsPerBlock = 256;
    private const int ElementsPerBlock = ThreadsPerBlock * 2;
    private const int Blocks = N / ElementsPerBlock;

    private float[] _input = null!;
    private float[] _outputCpu = null!;

    private GpuBuffer<float>? _dInput;
    private GpuBuffer<float>? _dOutput;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _input = new float[N];
        _outputCpu = new float[Blocks];

        var rand = new Random(42);
        for (var i = 0; i < N; i++)
        {
            _input[i] = (float)rand.NextDouble();
        }

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dInput = new GpuBuffer<float>(N);
        _dOutput = new GpuBuffer<float>(Blocks);
        _dInput.CopyToDevice(_input);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dInput?.Dispose();
        _dOutput?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public float Cpu()
    {
        for (var b = 0; b < Blocks; b++)
        {
            var sum = 0.0f;
            var start = b * ElementsPerBlock;
            var end = start + ElementsPerBlock;
            if (end > N)
            {
                end = N;
            }

            for (var i = start; i < end; i++)
            {
                sum += _input[i];
            }

            _outputCpu[b] = sum;
        }

        return _outputCpu.Sum();
    }

    [Benchmark]
    public void GSharpGpu()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        ReductionKernelsWrapper.Launch_ReductionKernel_Device(
            _dInput!.DevicePointer, _dOutput!.DevicePointer, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        timer.Stop();
        timer.Synchronize();

        // Memory transferred: Read N floats, Write Blocks floats
        double bytes = (N + Blocks) * 4.0;
        GpuMetricsContext.RecordBytes(bytes);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void NativeCuda()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchReduction_Device(
            _dInput!.DevicePointer, _dOutput!.DevicePointer, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        timer.Stop();
        timer.Synchronize();

        double bytes = (N + Blocks) * 4.0;
        GpuMetricsContext.RecordBytes(bytes);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }
}
