namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed class MemoryCopyBenchmark(int count = 10_000_000) : IComputeBenchmark
{
    private const int BlockSize = 256;

    private float[] _src = [];
    private float[] _dstCpu = [];
    private float[] _dstGsharp = [];
    private float[] _dstNative = [];

    public string Name => $"Memory Copy ({count:N0} floats)";

    // 1 read and 1 write per element
    public double TheoreticalBytes => 2.0 * count * sizeof(float);

    public void Initialize()
    {
        _src = new float[count];
        _dstCpu = new float[count];
        _dstGsharp = new float[count];
        _dstNative = new float[count];

        var rand = new Random(42);
        for (var i = 0; i < count; i++)
        {
            _src[i] = (float)rand.NextDouble();
        }
    }

    public void RunReferenceCpu()
    {
        for (var i = 0; i < count; i++)
        {
            _dstCpu[i] = _src[i];
        }
    }

    public void RunGSharp() =>
        MemoryCopyKernels.LaunchMemoryCopyKernel(_src, _dstGsharp, count, gridDimX: GridDim, blockDimX: BlockSize);

    public void RunNativeCuda() =>
        NativeKernelInvoker.LaunchMemoryCopy(_src, _dstNative, count, gridDimX: GridDim, blockDimX: BlockSize);

    public void Verify()
    {
        BenchmarkVerification.AssertEqual("G#", _dstCpu, _dstGsharp);
        BenchmarkVerification.AssertEqual("Native", _dstCpu, _dstNative);
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionMemoryCopy;
    public string NativeKernelName => "MemoryCopyKernel_Native";
    public string GSharpKernelName => "__gsharp_k_Henicos__Benchmarks__ComputeBenchmarks__MemoryCopyKernels__MemoryCopyKernel";

    private uint GridDim => (uint)((count + BlockSize - 1) / BlockSize);
}
