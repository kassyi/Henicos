namespace Henicos.Benchmarks.ComputeBenchmarks;

public class SgemmBenchmark : IComputeBenchmark
{
    private const int N = 512; // 512x512 matrix
    private const int TileSize = 16;

    private float[] _a = [];
    private float[] _b = [];
    private float[] _cCpu = [];
    private float[] _cGsharp = [];
    private float[] _cNative = [];

    public string Name => "SGEMM (512x512)";

    public void Initialize()
    {
        _a = new float[N * N];
        _b = new float[N * N];
        _cCpu = new float[N * N];
        _cGsharp = new float[N * N];
        _cNative = new float[N * N];

        var rand = new Random(42);
        for (var i = 0; i < N * N; i++)
        {
            _a[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
            _b[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
        }
    }

    public void RunReferenceCpu()
    {
        for (var row = 0; row < N; row++)
        {
            for (var col = 0; col < N; col++)
            {
                var sum = 0.0f;
                for (var k = 0; k < N; k++)
                {
                    sum += _a[row * N + k] * _b[k * N + col];
                }

                _cCpu[row * N + col] = sum;
            }
        }
    }

    public void RunGSharp()
    {
        uint gridDimX = (N + TileSize - 1) / TileSize;
        uint gridDimY = (N + TileSize - 1) / TileSize;
        uint blockDimX = TileSize;
        uint blockDimY = TileSize;

        SgemmKernels.LaunchSgemmKernel(
            _a, _b, _cGsharp, N,
            gridDimX: gridDimX, gridDimY: gridDimY, blockDimX: blockDimX, blockDimY: blockDimY);
    }

    public void RunNativeCuda()
    {
        uint gridDimX = (N + TileSize - 1) / TileSize;
        uint gridDimY = (N + TileSize - 1) / TileSize;
        uint blockDimX = TileSize;
        uint blockDimY = TileSize;

        NativeKernelInvoker.LaunchSgemm(
            _a, _b, _cNative, N,
            gridDimX: gridDimX, gridDimY: gridDimY, blockDimX: blockDimX, blockDimY: blockDimY);
    }

    public void Verify()
    {
        for (var i = 0; i < N * N; i++)
        {
            if (Math.Abs(_cCpu[i] - _cGsharp[i]) > 1e-2f)
            {
                throw new Exception($"Mismatch at index {i}: CPU={_cCpu[i]}, G#={_cGsharp[i]}");
            }

            if (Math.Abs(_cCpu[i] - _cNative[i]) > 1e-2f)
            {
                throw new Exception($"Mismatch at index {i}: CPU={_cCpu[i]}, Native={_cNative[i]}");
            }
        }
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionSgemm;
    public string NativeKernelName => "SgemmKernel_Native";
    public string GSharpKernelName => "SgemmKernels_SgemmKernel";
}
