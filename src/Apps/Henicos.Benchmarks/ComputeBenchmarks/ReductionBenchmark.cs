namespace Henicos.Benchmarks.ComputeBenchmarks;

public class ReductionBenchmark : IComputeBenchmark
{
    private const int NFloats = 16 * 1024 * 1024; // 16M floats
    private const int N = NFloats / 4; // 4M Float4 elements
    private const int ThreadsPerBlock = 256;
    private const int ElementsPerBlock = ThreadsPerBlock * 2;
    private const int Blocks = N / ElementsPerBlock;

    private Float4[] _input = null!;
    private float[] _outputCpu = null!;
    private float[] _outputGsharp = null!;
    private float[] _outputNative = null!;

    private float _finalResultCpu;
    private float _finalResultGsharp;
    private float _finalResultNative;

    public string Name => "Parallel Reduction";

    public void Initialize()
    {
        _input = new Float4[N];
        _outputCpu = new float[Blocks];
        _outputGsharp = new float[Blocks];
        _outputNative = new float[Blocks];

        var rand = new Random(42);
        for (var i = 0; i < N; i++)
        {
            _input[i] = new Float4(
                (float)rand.NextDouble(),
                (float)rand.NextDouble(),
                (float)rand.NextDouble(),
                (float)rand.NextDouble()
            );
        }
    }

    public void RunReferenceCpu()
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
                var v = _input[i];
                sum += v.X + v.Y + v.Z + v.W;
            }

            _outputCpu[b] = sum;
        }

        _finalResultCpu = Sum(_outputCpu);
    }

    public void RunGSharp()
    {
        ReductionKernelsWrapper.LaunchReductionKernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultGsharp = Sum(_outputGsharp);
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchReduction(
            _input, _outputNative, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultNative = Sum(_outputNative);
    }

    private static float Sum(float[] array)
    {
        var sum = 0f;
        foreach (var t in array)
        {
            sum += t;
        }

        return sum;
    }

    public void Verify()
    {
        for (var b = 0; b < Blocks; b++)
        {
            var blockDiffGsharp = Math.Abs(_outputCpu[b] - _outputGsharp[b]);
            if (blockDiffGsharp > 1e-1f)
            {
                throw new Exception(
                    $"Mismatch in block {b} G# reduction sum: CPU={_outputCpu[b]}, G#={_outputGsharp[b]}, Diff={blockDiffGsharp}");
            }

            var blockDiffNative = Math.Abs(_outputCpu[b] - _outputNative[b]);
            if (blockDiffNative > 1e-1f)
            {
                throw new Exception(
                    $"Mismatch in block {b} Native reduction sum: CPU={_outputCpu[b]}, Native={_outputNative[b]}, Diff={blockDiffNative}");
            }
        }

        var diffGsharp = Math.Abs(_finalResultCpu - _finalResultGsharp);
        var threshold = 1.0f;

        if (diffGsharp > threshold)
        {
            throw new Exception(
                $"Mismatch in final G# reduction sum: CPU={_finalResultCpu}, G#={_finalResultGsharp}, Diff={diffGsharp}");
        }

        var diffNative = Math.Abs(_finalResultCpu - _finalResultNative);
        if (diffNative > threshold)
        {
            throw new Exception(
                $"Mismatch in final Native reduction sum: CPU={_finalResultCpu}, Native={_finalResultNative}, Diff={diffNative}");
        }
    }
}
