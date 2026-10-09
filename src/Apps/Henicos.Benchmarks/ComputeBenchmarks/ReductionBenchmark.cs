
namespace Henicos.Benchmarks.ComputeBenchmarks;

public class ReductionBenchmark : IComputeBenchmark
{
    private const int NFloats = 16 * 1024 * 1024; // 16M floats
    private const int N = NFloats / 4; // 4M Float4 elements
    private const int ThreadsPerBlock = 256;
    private const int ElementsPerBlock = ThreadsPerBlock * 2;
    private const int Blocks = N / ElementsPerBlock;

    private Float4[] _input = [];
    private float[] _outputCpu = [];
    private float[] _outputGsharp = [];
    private float[] _outputNative = [];

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
            var start = b * ElementsPerBlock;
            var end = start + ElementsPerBlock;
            if (end > N)
            {
                end = N;
            }

            _outputCpu[b] = _input.AsSpan(start, end - start).AsStackEnumerable().Sum(v => v.X + v.Y + v.Z + v.W);
        }

        _finalResultCpu = _outputCpu.AsStackEnumerable().Sum();
    }

    public void RunGSharp()
    {
        ReductionKernels.LaunchReductionKernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultGsharp = _outputGsharp.AsStackEnumerable().Sum();
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchReduction(
            _input, _outputNative, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        _finalResultNative = _outputNative.AsStackEnumerable().Sum();
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

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionReduction;
    public string NativeKernelName => "ReductionKernel_Native";
    public string GSharpKernelName => "ReductionKernels_ReductionKernel";
}
