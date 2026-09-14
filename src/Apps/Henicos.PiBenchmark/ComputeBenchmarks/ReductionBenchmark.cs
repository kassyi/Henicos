namespace Henicos.PiBenchmark.ComputeBenchmarks;

public class ReductionBenchmark : IComputeBenchmark
{
    private const int N = 16 * 1024 * 1024; // 16M elements
    private const int ThreadsPerBlock = 256;
    private const int ElementsPerBlock = ThreadsPerBlock * 2;
    private const int Blocks = N / ElementsPerBlock;

    private float[] _input = null!;
    private float[] _outputCpu = null!;
    private float[] _outputGsharp = null!;
    
    private float _finalResultCpu;
    private float _finalResultGsharp;

    public string Name => "Parallel Reduction";

    public void Initialize()
    {
        _input = new float[N];
        _outputCpu = new float[Blocks];
        _outputGsharp = new float[Blocks];

        var rand = new Random(42);
        for (int i = 0; i < N; i++)
        {
            _input[i] = (float)rand.NextDouble();
        }
    }

    public void RunReferenceCpu()
    {
        // Simple CPU baseline (sequential for fairness of what a standard C# dev would write, or simple parallel)
        // Here we do a manual chunked sum to simulate the block structure
        Parallel.For(0, Blocks, b =>
        {
            float sum = 0;
            int start = b * ElementsPerBlock;
            int end = start + ElementsPerBlock;
            for (int i = start; i < end; i++)
            {
                sum += _input[i];
            }
            _outputCpu[b] = sum;
        });

        // Final CPU reduction on the host
        _finalResultCpu = _outputCpu.Sum();
    }

    public void RunGSharpCpu()
    {
        // Execute G# Kernel on CPU Jit Engine
        ReductionKernelsWrapper.Launch_ReductionKernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        // Final CPU reduction on the host
        _finalResultGsharp = _outputGsharp.Sum();
    }

    public void Verify()
    {
        // Floating point accumulation order matters, so we allow a generous epsilon for 16M additions
        float diff = Math.Abs(_finalResultCpu - _finalResultGsharp);
        float threshold = 1.0f; // High threshold because 16M floats sum can drift significantly based on tree vs sequential

        if (diff > threshold)
        {
            throw new Exception($"Mismatch in reduction sum: CPU={_finalResultCpu}, G#={_finalResultGsharp}, Diff={diff}");
        }
    }
}
