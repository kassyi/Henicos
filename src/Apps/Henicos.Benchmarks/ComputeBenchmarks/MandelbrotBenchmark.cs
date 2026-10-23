namespace Henicos.Benchmarks.ComputeBenchmarks;

public sealed class MandelbrotBenchmark(int size = 1024, int maxIter = 1000) : IComputeBenchmark
{
    private const int TileSize = 16;
    private const float XMin = -2.0f;
    private const float XMax = 1.0f;
    private const float YMin = -1.5f;
    private const float YMax = 1.5f;

    // Float rounding differs between the host and fused device arithmetic, so a boundary pixel can escape one
    // iteration earlier or later.
    private const double MaxCpuMismatchRatio = 0.01;
    private const double MaxNativeMismatchRatio = 0.001;

    private int[] _outputCpu = [];
    private int[] _outputGsharp = [];
    private int[] _outputNative = [];

    public string Name => $"Mandelbrot ({size}x{size}, {maxIter} iter)";

    public void Initialize()
    {
        _outputCpu = new int[size * size];
        _outputGsharp = new int[size * size];
        _outputNative = new int[size * size];
    }

    public void RunReferenceCpu()
    {
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                var x0 = XMin + (XMax - XMin) * col / size;
                var y0 = YMin + (YMax - YMin) * row / size;
                var x = 0.0f;
                var y = 0.0f;
                var iter = 0;

                while (x * x + y * y <= 4.0f && iter < maxIter)
                {
                    var xtemp = x * x - y * y + x0;
                    y = 2.0f * x * y + y0;
                    x = xtemp;
                    iter++;
                }

                _outputCpu[row * size + col] = iter;
            }
        }
    }

    public void RunGSharp() =>
        MandelbrotKernels.LaunchMandelbrotKernel(
            _outputGsharp, size, size, maxIter, XMin, XMax, YMin, YMax,
            gridDimX: GridDim, gridDimY: GridDim, blockDimX: TileSize, blockDimY: TileSize);

    public void RunNativeCuda() =>
        NativeKernelInvoker.LaunchMandelbrot(
            _outputNative, size, size, maxIter, XMin, XMax, YMin, YMax,
            gridDimX: GridDim, gridDimY: GridDim, blockDimX: TileSize, blockDimY: TileSize);

    public void Verify()
    {
        VerifyCloseToCpu("G#", _outputGsharp);
        VerifyCloseToCpu("Native", _outputNative);

        var mismatches = 0;
        for (var i = 0; i < _outputGsharp.Length; i++)
        {
            if (_outputGsharp[i] != _outputNative[i])
            {
                mismatches++;
            }
        }

        if (mismatches > _outputGsharp.Length * MaxNativeMismatchRatio)
        {
            throw new InvalidOperationException(
                $"G# and Native Mandelbrot differ in {mismatches} of {_outputGsharp.Length} pixels");
        }
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionMandelbrot;
    public string NativeKernelName => "MandelbrotKernel_Native";
    public string GSharpKernelName => "__gsharp_k_Henicos__Benchmarks__ComputeBenchmarks__MandelbrotKernels__MandelbrotKernel";

    private uint GridDim => (uint)((size + TileSize - 1) / TileSize);

    private void VerifyCloseToCpu(string label, int[] actual)
    {
        var mismatches = 0;
        for (var i = 0; i < actual.Length; i++)
        {
            if (actual[i] != _outputCpu[i])
            {
                mismatches++;
            }
        }

        if (mismatches > actual.Length * MaxCpuMismatchRatio)
        {
            throw new InvalidOperationException(
                $"{label} Mandelbrot differs from the CPU reference in {mismatches} of {actual.Length} pixels");
        }
    }
}
