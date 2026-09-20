using BenchmarkDotNet.Attributes;
using Henicos.Benchmarks.Infrastructure;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Benchmarks.Algorithms;

[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class BlackScholesBenchmark
{
    private const int N = 1_000_000;

    private float[] _s = null!;
    private float[] _x = null!;
    private float[] _t = null!;
    private float _r;
    private float _v;

    private float[] _callResultCpu = null!;
    private float[] _putResultCpu = null!;

    private GpuBuffer<float>? _dS;
    private GpuBuffer<float>? _dX;
    private GpuBuffer<float>? _dT;
    private GpuBuffer<float>? _dCall;
    private GpuBuffer<float>? _dPut;

    private GpuKernelLoader.CudaContextScope _ctxScope;

    [GlobalSetup]
    public void Setup()
    {
        _s = new float[N];
        _x = new float[N];
        _t = new float[N];
        _callResultCpu = new float[N];
        _putResultCpu = new float[N];

        _r = 0.02f;
        _v = 0.30f;

        var rand = new Random(42);
        for (int i = 0; i < N; i++)
        {
            _s[i] = (float)(rand.NextDouble() * 100.0 + 10.0); // Spot price 10..110
            _x[i] = (float)(rand.NextDouble() * 100.0 + 10.0); // Strike price 10..110
            _t[i] = (float)(rand.NextDouble() * 5.0 + 0.1); // Time to maturity 0.1..5.1
        }

        _ctxScope = new GpuKernelLoader.CudaContextScope();
        _dS = new GpuBuffer<float>(N);
        _dX = new GpuBuffer<float>(N);
        _dT = new GpuBuffer<float>(N);
        _dCall = new GpuBuffer<float>(N);
        _dPut = new GpuBuffer<float>(N);

        _dS.CopyToDevice(_s);
        _dX.CopyToDevice(_x);
        _dT.CopyToDevice(_t);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dS?.Dispose();
        _dX?.Dispose();
        _dT?.Dispose();
        _dCall?.Dispose();
        _dPut?.Dispose();
        _ctxScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Cpu()
    {
        for (int i = 0; i < N; i++)
        {
            BlackScholesCore(_s[i], _x[i], _t[i], _r, _v, out _callResultCpu[i], out _putResultCpu[i]);
        }
    }

    [Benchmark]
    public void GSharpGpu()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        uint blockSize = 256;
        uint numBlocks = (N + blockSize - 1) / blockSize;
        BlackScholesKernelsWrapper.LaunchBlackScholesKernel_Device(
            _dS!.DevicePointer, _dX!.DevicePointer, _dT!.DevicePointer, _r, _v,
            _dCall!.DevicePointer, _dPut!.DevicePointer, N,
            gridDimX: numBlocks, blockDimX: blockSize);

        timer.Stop();
        timer.Synchronize();

        // Rough estimate of ~100 FLOPs per option
        GpuMetricsContext.RecordFlops(N * 100.0);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    [Benchmark]
    public void NativeCuda()
    {
        using var timer = new CudaEventTimer();
        timer.Start();

        uint blockSize = 256;
        uint numBlocks = (N + blockSize - 1) / blockSize;
        NativeKernelInvoker.LaunchBlackScholes_Device(
            _dS!.DevicePointer, _dX!.DevicePointer, _dT!.DevicePointer, _r, _v,
            _dCall!.DevicePointer, _dPut!.DevicePointer, N,
            gridDimX: numBlocks, blockDimX: blockSize);

        timer.Stop();
        timer.Synchronize();

        GpuMetricsContext.RecordFlops(N * 100.0);
        GpuMetricsContext.RecordGpuTimeUs(timer.GetElapsedTimeMilliseconds() * 1000.0);
    }

    // Baseline C# Implementation
    private static void BlackScholesCore(float s, float x, float t, float r, float v, out float callResult,
        out float putResult)
    {
        float d1 = (MathF.Log(s / x) + (r + v * v / 2.0f) * t) / (v * MathF.Sqrt(t));
        float d2 = d1 - v * MathF.Sqrt(t);

        float cndD1 = Cnd(d1);
        float cndD2 = Cnd(d2);

        float expRt = MathF.Exp(-r * t);

        callResult = s * cndD1 - x * expRt * cndD2;
        putResult = x * expRt * (1.0f - cndD2) - s * (1.0f - cndD1);
    }

    private static float Cnd(float d)
    {
        const float A1 = 0.31938153f;
        const float A2 = -0.356563782f;
        const float A3 = 1.781477937f;
        const float A4 = -1.821255978f;
        const float A5 = 1.330274429f;
        const float Rsqrt2Pi = 0.39894228040143267793994605993438f;

        float k = 1.0f / (1.0f + 0.2316419f * MathF.Abs(d));
        float cnd = Rsqrt2Pi * MathF.Exp(-0.5f * d * d) *
                    (k * (A1 + k * (A2 + k * (A3 + k * (A4 + k * A5)))));

        if (d > 0)
        {
            cnd = 1.0f - cnd;
        }

        return cnd;
    }
}
