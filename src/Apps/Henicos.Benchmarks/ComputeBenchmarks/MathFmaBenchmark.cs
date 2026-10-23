namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed class MathFmaBenchmark(int count = 1_000_000, int iterations = 1000) : IComputeBenchmark
{
    private const int BlockSize = 256;
    private const float B = 1.00001f;
    private const float C = 0.00001f;

    // The device fuses a * b + c; the host rounds the product first.
    private const float Tolerance = 1e-3f;

    private float[] _input = [];
    private float[] _dataCpu = [];
    private float[] _dataGsharp = [];
    private float[] _dataNative = [];

    public string Name => $"Math FMA ({count:N0} x {iterations})";

    // count elements * iterations loops * 2 FLOPs per loop (1 mul, 1 add)
    public double TheoreticalFlops => 2.0 * count * iterations;

    public void Initialize()
    {
        _input = new float[count];
        _dataCpu = new float[count];
        _dataGsharp = new float[count];
        _dataNative = new float[count];

        var rand = new Random(42);
        for (var i = 0; i < count; i++)
        {
            _input[i] = (float)rand.NextDouble();
        }
    }

    public void RunReferenceCpu()
    {
        for (var i = 0; i < count; i++)
        {
            var a = _input[i];
            for (var it = 0; it < iterations; it++)
            {
                a = a * B + C;
            }

            _dataCpu[i] = a;
        }
    }

    public void RunGSharp()
    {
        Array.Copy(_input, _dataGsharp, count);
        MathFmaKernels.LaunchMathFmaKernel(_dataGsharp, count, iterations, gridDimX: GridDim, blockDimX: BlockSize);
    }

    public void RunNativeCuda()
    {
        Array.Copy(_input, _dataNative, count);
        NativeKernelInvoker.LaunchMathFma(_dataNative, count, iterations, gridDimX: GridDim, blockDimX: BlockSize);
    }

    public void Verify()
    {
        BenchmarkVerification.AssertClose("G#", _dataCpu, _dataGsharp, Tolerance);
        BenchmarkVerification.AssertClose("Native", _dataCpu, _dataNative, Tolerance);
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionMathFma;
    public string NativeKernelName => "MathFmaKernel_Native";
    public string GSharpKernelName => "__gsharp_k_Henicos__Benchmarks__ComputeBenchmarks__MathFmaKernels__MathFmaKernel";

    private uint GridDim => (uint)((count + BlockSize - 1) / BlockSize);
}
