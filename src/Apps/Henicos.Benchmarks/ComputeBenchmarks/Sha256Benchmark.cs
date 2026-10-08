namespace Henicos.Benchmarks.ComputeBenchmarks;

/// <summary>
/// Benchmarks raw SHA-256 single-block transformation / compression function compute throughput.
/// </summary>
public class Sha256Benchmark : IComputeBenchmark
{
    private const int N = 100_000; // 100,000 blocks of 64 bytes
    private const int ThreadsPerBlock = 256;
    private const int Blocks = (N + ThreadsPerBlock - 1) / ThreadsPerBlock;

    private byte[] _input = [];
    private uint[] _outputCpu = [];
    private uint[] _outputGsharp = [];
    private uint[] _outputNative = [];

    public string Name => "SHA-256 Compression Function (100k Blocks)";

    public void Initialize()
    {
        _input = new byte[N * 64];
        _outputCpu = new uint[N * 8];
        _outputGsharp = new uint[N * 8];
        _outputNative = new uint[N * 8];

        var rand = new Random(42);
        rand.NextBytes(_input);
    }

    public void RunReferenceCpu()
    {
        for (var i = 0; i < N; i++)
        {
            Sha256Kernels.ComputeBlock(i, _input, _outputCpu);
        }
    }

    public void RunGSharp()
    {
        Sha256Kernels.LaunchSha256Kernel(
            _input, _outputGsharp, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchSha256(
            _input, _outputNative, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);
    }

    public void Verify()
    {
        if (!_outputCpu.AsSpan().SequenceEqual(_outputGsharp))
        {
            for (var i = 0; i < N * 8; i++)
            {
                if (_outputCpu[i] != _outputGsharp[i])
                {
                    throw new Exception(
                        $"Mismatch at hash output index {i}: CPU={_outputCpu[i]:X8}, G#={_outputGsharp[i]:X8}");
                }
            }
        }

        if (!_outputCpu.AsSpan().SequenceEqual(_outputNative))
        {
            for (var i = 0; i < N * 8; i++)
            {
                if (_outputCpu[i] != _outputNative[i])
                {
                    throw new Exception(
                        $"Mismatch at hash output index {i}: CPU={_outputCpu[i]:X8}, Native={_outputNative[i]:X8}");
                }
            }
        }
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionSha256;
    public string NativeKernelName => "Sha256Kernel_Native";
    public string GSharpKernelName => "Sha256Kernels_Sha256Kernel";
}
