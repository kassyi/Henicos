namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed class BranchDivergenceBenchmark(int count = 1_000_000) : IComputeBenchmark
{
    private const int BlockSize = 256;
    private const float Tolerance = 1e-3f;

    private float[] _input = [];
    private float[] _dataCpu = [];
    private float[] _dataGsharp = [];
    private float[] _dataNative = [];

    public string Name => $"Branch Divergence ({count:N0} elements)";

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
            var val = _input[i];
            if (i % 2 == 0)
            {
                for (var j = 0; j < 100; j++)
                {
                    val = val * 1.01f + 0.01f;
                }
            }
            else
            {
                for (var j = 0; j < 100; j++)
                {
                    val = val * 0.99f - 0.01f;
                }
            }

            _dataCpu[i] = val;
        }
    }

    public void RunGSharp()
    {
        Array.Copy(_input, _dataGsharp, count);
        BranchDivergenceKernels.LaunchBranchDivergenceKernel(_dataGsharp, count, gridDimX: GridDim, blockDimX: BlockSize);
    }

    public void RunNativeCuda()
    {
        Array.Copy(_input, _dataNative, count);
        NativeKernelInvoker.LaunchBranchDivergence(_dataNative, count, gridDimX: GridDim, blockDimX: BlockSize);
    }

    public void Verify()
    {
        BenchmarkVerification.AssertClose("G#", _dataCpu, _dataGsharp, Tolerance);
        BenchmarkVerification.AssertClose("Native", _dataCpu, _dataNative, Tolerance);
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionBranchDivergence;
    public string NativeKernelName => "BranchDivergenceKernel_Native";
    public string GSharpKernelName => "__gsharp_k_Henicos__Benchmarks__ComputeBenchmarks__BranchDivergenceKernels__BranchDivergenceKernel";

    private uint GridDim => (uint)((count + BlockSize - 1) / BlockSize);
}
