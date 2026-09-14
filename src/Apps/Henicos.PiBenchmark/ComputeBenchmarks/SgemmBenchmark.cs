namespace Henicos.PiBenchmark.ComputeBenchmarks;

public class SgemmBenchmark : IComputeBenchmark
{
    private const int N = 512; // 512x512 matrix
    private const int TileSize = 16;

    private float[] _a = null!;
    private float[] _b = null!;
    private float[] _cCpu = null!;
    private float[] _cGsharp = null!;

    public string Name => "SGEMM (512x512, Tiled)";

    public void Initialize()
    {
        _a = new float[N * N];
        _b = new float[N * N];
        _cCpu = new float[N * N];
        _cGsharp = new float[N * N];

        var rand = new Random(42);
        for (int i = 0; i < N * N; i++)
        {
            _a[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
            _b[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
        }
    }

    public void RunReferenceCpu()
    {
        // Simple CPU baseline
        Parallel.For(0, N, row =>
        {
            for (int col = 0; col < N; col++)
            {
                float sum = 0;
                for (int k = 0; k < N; k++)
                {
                    sum += _a[row * N + k] * _b[k * N + col];
                }

                _cCpu[row * N + col] = sum;
            }
        });
    }

    public void RunGSharpCpu()
    {
        uint gridDimX = (uint)((N + TileSize - 1) / TileSize);
        uint gridDimY = (uint)((N + TileSize - 1) / TileSize);
        uint blockDimX = TileSize;
        uint blockDimY = TileSize;

        // Execute G# Kernel on CPU Jit Engine
        SgemmKernelsWrapper.Launch_SgemmKernel(
            _a, _b, _cGsharp, N,
            gridDimX: gridDimX, gridDimY: gridDimY, blockDimX: blockDimX, blockDimY: blockDimY);
    }

    public void Verify()
    {
        for (int i = 0; i < N * N; i++)
        {
            if (Math.Abs(_cCpu[i] - _cGsharp[i]) > 1e-2f)
            {
                throw new Exception($"Mismatch at index {i}: CPU={_cCpu[i]}, G#={_cGsharp[i]}");
            }
        }
    }
}
