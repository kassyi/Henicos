namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed class TransposeBenchmark(int size = 2048) : IComputeBenchmark
{
    private const int TileSize = 16;

    private float[] _input = [];
    private float[] _outputCpu = [];
    private float[] _outputGsharp = [];
    private float[] _outputNative = [];

    public string Name => $"Transpose ({size}x{size})";

    // 1 read and 1 write per element
    public double TheoreticalBytes => 2.0 * size * size * sizeof(float);

    public void Initialize()
    {
        _input = new float[size * size];
        _outputCpu = new float[size * size];
        _outputGsharp = new float[size * size];
        _outputNative = new float[size * size];

        var rand = new Random(42);
        for (var i = 0; i < _input.Length; i++)
        {
            _input[i] = (float)rand.NextDouble();
        }
    }

    public void RunReferenceCpu()
    {
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                _outputCpu[col * size + row] = _input[row * size + col];
            }
        }
    }

    public void RunGSharp() =>
        TransposeKernels.LaunchTransposeKernel(
            _input, _outputGsharp, size, size,
            gridDimX: GridDim, gridDimY: GridDim, blockDimX: TileSize, blockDimY: TileSize);

    public void RunNativeCuda() =>
        NativeKernelInvoker.LaunchTranspose(
            _input, _outputNative, size, size,
            gridDimX: GridDim, gridDimY: GridDim, blockDimX: TileSize, blockDimY: TileSize);

    public void Verify()
    {
        BenchmarkVerification.AssertEqual("G#", _outputCpu, _outputGsharp);
        BenchmarkVerification.AssertEqual("Native", _outputCpu, _outputNative);
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionTranspose;
    public string NativeKernelName => "TransposeKernel_Native";
    public string GSharpKernelName => "__gsharp_k_Henicos__Benchmarks__ComputeBenchmarks__TransposeKernels__TransposeKernel";

    private uint GridDim => (uint)((size + TileSize - 1) / TileSize);
}
