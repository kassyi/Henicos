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
    private float[] _outputNative = null!;

    private float _finalResultCpu;
    private float _finalResultGsharp;
    private float _finalResultNative;

    public string Name => "Parallel Reduction";

    public void Initialize()
    {
        _input = new float[N];
        _outputCpu = new float[Blocks];
        _outputGsharp = new float[Blocks];
        _outputNative = new float[Blocks];

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

    public void RunGSharp()
    {
        ReductionKernelsWrapper.Launch_ReductionKernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultGsharp = _outputGsharp.Sum();
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchReduction(
            _input, _outputNative, N,
            gridDimX: (uint)Blocks, blockDimX: ThreadsPerBlock);

        _finalResultNative = _outputNative.Sum();
    }

    public void Verify()
    {
        for (int b = 0; b < Blocks; b++)
        {
            float blockDiffGsharp = Math.Abs(_outputCpu[b] - _outputGsharp[b]);
            if (blockDiffGsharp > 1e-3f)
            {
                throw new Exception(
                    $"Mismatch in block {b} G# reduction sum: CPU={_outputCpu[b]}, G#={_outputGsharp[b]}, Diff={blockDiffGsharp}");
            }

            float blockDiffNative = Math.Abs(_outputCpu[b] - _outputNative[b]);
            if (blockDiffNative > 1e-3f)
            {
                throw new Exception(
                    $"Mismatch in block {b} Native reduction sum: CPU={_outputCpu[b]}, Native={_outputNative[b]}, Diff={blockDiffNative}");
            }
        }

        float diffGsharp = Math.Abs(_finalResultCpu - _finalResultGsharp);
        float threshold = 1.0f;

        if (diffGsharp > threshold)
        {
            throw new Exception($"Mismatch in final G# reduction sum: CPU={_finalResultCpu}, G#={_finalResultGsharp}, Diff={diffGsharp}");
        }

        float diffNative = Math.Abs(_finalResultCpu - _finalResultNative);
        if (diffNative > threshold)
        {
            throw new Exception($"Mismatch in final Native reduction sum: CPU={_finalResultCpu}, Native={_finalResultNative}, Diff={diffNative}");
        }
    }
}
