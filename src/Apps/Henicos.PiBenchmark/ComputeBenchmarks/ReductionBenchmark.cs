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
        for (int b = 0; b < Blocks; b++)
        {
            float sum = 0.0f;
            int start = b * ElementsPerBlock;
            int end = start + ElementsPerBlock;
            if (end > N)
            {
                end = N;
            }

            for (int i = start; i < end; i++)
            {
                sum += _input[i];
            }

            _outputCpu[b] = sum;
        }

        _finalResultCpu = _outputCpu.Sum();
    }

    public void RunGSharpCpu()
    {
        ReductionKernelsWrapper.Launch_ReductionKernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultGsharp = _outputGsharp.Sum();
    }

    public void Verify()
    {
        for (int b = 0; b < Blocks; b++)
        {
            float blockDiff = Math.Abs(_outputCpu[b] - _outputGsharp[b]);
            if (blockDiff > 1e-3f)
            {
                throw new Exception(
                    $"Mismatch in block {b} reduction sum: CPU={_outputCpu[b]}, G#={_outputGsharp[b]}, Diff={blockDiff}");
            }
        }

        float diff = Math.Abs(_finalResultCpu - _finalResultGsharp);
        float threshold = 1.0f;

        if (diff > threshold)
        {
            throw new Exception($"Mismatch in final reduction sum: CPU={_finalResultCpu}, G#={_finalResultGsharp}, Diff={diff}");
        }
    }
}
