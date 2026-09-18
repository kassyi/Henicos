using Kassyi.GSharp.Runtime.Interop;

namespace Henicos.PiBenchmark.ComputeBenchmarks;

using Kassyi.GSharp.Runtime;

public class SgemmBenchmark : IComputeBenchmark
{
    private const int N = 512; // 512x512 matrix
    private const int TileSize = 16;

    private const uint GridDimX = (N + TileSize - 1) / TileSize;
    private const uint GridDimY = (N + TileSize - 1) / TileSize;
    private const uint BlockDimX = TileSize;
    private const uint BlockDimY = TileSize;

    private float[] _a = null!;
    private float[] _b = null!;
    private float[] _cCpu = null!;
    private float[] _cGsharp = null!;
    private float[] _cNative = null!;

    private GpuBuffer<float>? _dA;
    private GpuBuffer<float>? _dB;
    private GpuBuffer<float>? _dC;

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
        SgemmKernelsWrapper.Launch_SgemmKernel(
            _a, _b, _cGsharp, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchSgemm(
            _a, _b, _cNative, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);
    }

    public void InitializeDeviceMemory()
    {
        _dA = new GpuBuffer<float>(N * N);
        _dB = new GpuBuffer<float>(N * N);
        _dC = new GpuBuffer<float>(N * N);

        _dA.CopyToDevice(_a);
        _dB.CopyToDevice(_b);
    }

    public void CleanupDeviceMemory()
    {
        if (_dC != null)
        {
            _dC.CopyToHost(_cGsharp); // Get result for verification if needed
        }

        _dA?.Dispose();
        _dB?.Dispose();
        _dC?.Dispose();
    }

    public void RunGSharp_Pure()
    {
        if (_dA == null || _dB == null || _dC == null)
        {
            return;
        }

        SgemmKernelsWrapper.Launch_SgemmKernel_Device(
            _dA.DevicePointer, _dB.DevicePointer, _dC.DevicePointer, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);

        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    public void RunNativeCuda_Pure()
    {
        if (_dA == null || _dB == null || _dC == null)
        {
            return;
        }

        NativeKernelInvoker.LaunchSgemm_Device(
            _dA.DevicePointer, _dB.DevicePointer, _dC.DevicePointer, N,
            gridDimX: GridDimX, gridDimY: GridDimY, blockDimX: BlockDimX, blockDimY: BlockDimY);
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
}
