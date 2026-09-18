using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Algorithms;

[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class Sha256Benchmark
{
    private const int N = 100_000; // 100,000 blocks of 64 bytes
    private const int ThreadsPerBlock = 256;
    private const int Blocks = (N + ThreadsPerBlock - 1) / ThreadsPerBlock;

    private static readonly uint[] s_k =
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    ];

    private byte[] _input = null!;
    private uint[] _outputCpu = null!;

    private GpuBuffer<byte>? _dInput;
    private GpuBuffer<uint>? _dOutput;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _input = new byte[N * 64];
        _outputCpu = new uint[N * 8];

        var rand = new Random(42);
        rand.NextBytes(_input);

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dInput = new GpuBuffer<byte>(N * 64);
        _dOutput = new GpuBuffer<uint>(N * 8);
        _dInput.CopyToDevice(_input);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dInput?.Dispose();
        _dOutput?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        for (var i = 0; i < N; i++)
        {
            ReferenceSha256(i);
        }
    }

    [Benchmark]
    public void GSharpGpu()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        Sha256KernelsWrapper.Launch_Sha256Kernel_Device(
            _dInput!.DevicePointer, _dOutput!.DevicePointer, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        timer.Stop();
        timer.Synchronize();

        // Approximate operations for SHA-256 compression function (roughly 1000 per block)
        GpuMetricsContext.RecordFlops(N * 1000.0);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void NativeCuda()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        NativeKernelInvoker.LaunchSha256_Device(
            _dInput!.DevicePointer, _dOutput!.DevicePointer, N,
            gridDimX: Blocks, blockDimX: ThreadsPerBlock);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordFlops(N * 1000.0);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    private void ReferenceSha256(int tid)
    {
        var offset = tid * 64;
        Span<uint> w = stackalloc uint[64];

        for (var i = 0; i < 16; i++)
        {
            w[i] = (uint)(_input[offset + i * 4] << 24) |
                   (uint)(_input[offset + i * 4 + 1] << 16) |
                   (uint)(_input[offset + i * 4 + 2] << 8) |
                   _input[offset + i * 4 + 3];
        }

        for (var i = 16; i < 64; i++)
        {
            var s1 = RotR(w[i - 2], 17) ^ RotR(w[i - 2], 19) ^ (w[i - 2] >> 10);
            var s0 = RotR(w[i - 15], 7) ^ RotR(w[i - 15], 18) ^ (w[i - 15] >> 3);
            w[i] = s1 + w[i - 7] + s0 + w[i - 16];
        }

        uint a = 0x6a09e667,
            b = 0xbb67ae85,
            c = 0x3c6ef372,
            d = 0xa54ff53a,
            e = 0x510e527f,
            f = 0x9b05688c,
            g = 0x1f83d9ab,
            h = 0x5be0cd19;

        for (var i = 0; i < 64; i++)
        {
            var s1 = RotR(e, 6) ^ RotR(e, 11) ^ RotR(e, 25);
            var ch = (e & f) ^ (~e & g);
            var temp1 = h + s1 + ch + s_k[i] + w[i];
            var s0 = RotR(a, 2) ^ RotR(a, 13) ^ RotR(a, 22);
            var maj = (a & b) ^ (a & c) ^ (b & c);
            var temp2 = s0 + maj;

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        _outputCpu[tid * 8 + 0] = a + 0x6a09e667;
        _outputCpu[tid * 8 + 1] = b + 0xbb67ae85;
        _outputCpu[tid * 8 + 2] = c + 0x3c6ef372;
        _outputCpu[tid * 8 + 3] = d + 0xa54ff53a;
        _outputCpu[tid * 8 + 4] = e + 0x510e527f;
        _outputCpu[tid * 8 + 5] = f + 0x9b05688c;
        _outputCpu[tid * 8 + 6] = g + 0x1f83d9ab;
        _outputCpu[tid * 8 + 7] = h + 0x5be0cd19;
    }

    private static uint RotR(uint x, int n) => (x >> n) | (x << (32 - n));
}
