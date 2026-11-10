
namespace Henicos.Benchmarks;

[MemoryDiagnoser]
public class StackLinqGpuBenchmark
{
    private int[] _data = [];
    private int[] _results = [];
    private int _endMarker;

    [Params(10_000, 100_000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new int[Size];
        for (int i = 0; i < Size; i++)
        {
            _data[i] = i;
        }

        Array.Clear(_data, 0, Size / 10);

        _endMarker = _data[(int)(Size * 0.9)];
        
        _results = new int[256];
        
        // JIT/PTXコンパイルとデバイスのウォームアップ
        StackLinqGpuKernels.LaunchRawForHeavyPipelineKernel(_data, _results, blockDimX: 256);
        StackLinqGpuKernels.LaunchStackLinqHeavyPipelineKernel(_data, _results, blockDimX: 256);
        StackLinqGpuKernels.LaunchRawForDataWindowingKernel(_data, _endMarker, _results, blockDimX: 256);
        StackLinqGpuKernels.LaunchStackLinqDataWindowingKernel(_data, _endMarker, _results, blockDimX: 256);
    }

    // ==========================================
    // GPU カーネル: Heavy Pipeline
    // ==========================================

    [Benchmark(Description = "RawFor_HeavyPipeline_GPU")]
    public void RawForHeavyPipelineGpu() =>
        StackLinqGpuKernels.LaunchRawForHeavyPipelineKernel(_data, _results, blockDimX: 256);

    [Benchmark(Description = "StackLinq_HeavyPipeline_GPU")]
    public void StackLinqHeavyPipelineGpu() =>
        StackLinqGpuKernels.LaunchStackLinqHeavyPipelineKernel(_data, _results, blockDimX: 256);

    // ==========================================
    // GPU カーネル: Data Windowing
    // ==========================================

    [Benchmark(Description = "RawFor_DataWindowing_GPU")]
    public void RawForDataWindowingGpu() =>
        StackLinqGpuKernels.LaunchRawForDataWindowingKernel(_data, _endMarker, _results, blockDimX: 256);

    [Benchmark(Description = "StackLinq_DataWindowing_GPU")]
    public void StackLinqDataWindowingGpu() =>
        StackLinqGpuKernels.LaunchStackLinqDataWindowingKernel(_data, _endMarker, _results, blockDimX: 256);
}
