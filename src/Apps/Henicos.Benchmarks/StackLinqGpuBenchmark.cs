
namespace Henicos.Benchmarks;

[MemoryDiagnoser]
public class StackLinqGpuBenchmark
{
    private int[] _data = null!;
    private int[] _results = null!;
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
        StackLinqGpuKernels.LaunchRawFor_HeavyPipeline(_data, _results, blockDimX: 256);
        StackLinqGpuKernels.LaunchStackLinq_HeavyPipeline(_data, _results, blockDimX: 256);
        StackLinqGpuKernels.LaunchRawFor_DataWindowing(_data, _endMarker, _results, blockDimX: 256);
        StackLinqGpuKernels.LaunchStackLinq_DataWindowing(_data, _endMarker, _results, blockDimX: 256);
    }

    // ==========================================
    // GPU カーネル: Heavy Pipeline
    // ==========================================

    [Benchmark]
    public void RawFor_HeavyPipeline_GPU() => StackLinqGpuKernels.LaunchRawFor_HeavyPipeline(_data, _results, blockDimX: 256);

    [Benchmark]
    public void StackLinq_HeavyPipeline_GPU() => StackLinqGpuKernels.LaunchStackLinq_HeavyPipeline(_data, _results, blockDimX: 256);

    // ==========================================
    // GPU カーネル: Data Windowing
    // ==========================================

    [Benchmark]
    public void RawFor_DataWindowing_GPU() => StackLinqGpuKernels.LaunchRawFor_DataWindowing(_data, _endMarker, _results, blockDimX: 256);

    [Benchmark]
    public void StackLinq_DataWindowing_GPU() => StackLinqGpuKernels.LaunchStackLinq_DataWindowing(_data, _endMarker, _results, blockDimX: 256);
}
