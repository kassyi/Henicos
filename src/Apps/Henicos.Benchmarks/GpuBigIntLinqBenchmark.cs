using System.Numerics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

#nullable disable

namespace Henicos.Benchmarks;

/// <summary>
/// Benchmark comparing CPU (System.Numerics.BigInteger + PLINQ) vs GPU (GpuBigInt + StackLinq + CUDA Graph)
/// in realistic ZKP/FHE polynomial accumulation and cryptographic search scenarios.
/// </summary>
[Config(typeof(GpuBenchmarkConfig))]
[MemoryDiagnoser]
public class GpuBigIntLinqBenchmark : IDisposable
{
    private class GpuBenchmarkConfig : ManualConfig
    {
        public GpuBenchmarkConfig()
        {
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(3)
                .WithIterationCount(10)
                .AsDefault());
        }
    }

    private GpuKernelLoader.CudaContextScope _scope;

    private GpuWorkspace _context;

    private GpuBigInt[] _gpuScalars = [];
    private GpuBigInt _gpuConstant;

    private BigInteger[] _cpuScalars = [];
    private BigInteger _cpuConstant = BigInteger.Zero;

    private GpuBigIntExecutableGraph _precompiledGraph;

    private GpuBigIntArray _gpuVector;
    private GpuBigIntExecutableGraph _precompiledVectorGraph;

    [Params(256, 1024)]
    public int ElementCount { get; set; } = 256;

    [Params(8)] // 8 words = 256-bit (Standard ZKP / secp256k1 field size)
    public int NumWords { get; set; } = 8;

    [GlobalSetup]
    public void Setup()
    {
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024 * 1024);

        _gpuScalars = new GpuBigInt[ElementCount];
        _cpuScalars = new BigInteger[ElementCount];

        var rnd = new Random(42);
        _cpuConstant = 12345;
        _gpuConstant = CreateGpuBigInt(_cpuConstant, NumWords);

        var flatValues = new uint[ElementCount * NumWords];
        var flatSigns = new uint[ElementCount];

        for (var i = 0; i < ElementCount; i++)
        {
            var rawBytes = new byte[NumWords * 4];
            rnd.NextBytes(rawBytes);
            rawBytes[^1] &= 0x7F; // Positive BigInteger

            var val = new BigInteger(rawBytes);
            _cpuScalars[i] = val;

            var valBytes = val.ToByteArray();
            var uSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(valBytes);
            for (int j = 0; j < uSpan.Length && j < NumWords; j++)
            {
                flatValues[i * NumWords + j] = uSpan[j];
            }
            flatSigns[i] = val.Sign < 0 ? 1u : 0u;
        }

        _gpuVector = new GpuBigIntArray(_context, ElementCount, NumWords);
        _context.Provider.CopyHostToDevice(_gpuVector.Values.DevicePointer, flatValues, flatValues.Length, _context.Stream);
        _context.Provider.CopyHostToDevice(_gpuVector.Signs.DevicePointer, flatSigns, flatSigns.Length, _context.Stream);

        if (ElementCount <= 1024)
        {
            for (var i = 0; i < ElementCount; i++)
            {
                _gpuScalars[i] = CreateGpuBigInt(_cpuScalars[i], NumWords);
            }

            _precompiledGraph = _gpuScalars.AsStackEnumerable()
                .Select(s => s * _gpuConstant)
                .AsParallel()
                .Sum()
                .CompileToGraph(_context);
        }

        _precompiledVectorGraph = _gpuVector.AsVectorizedEnumerable()
            .SelectMultiply(_gpuConstant)
            .Sum()
            .CompileToGraph(_context);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        if (ElementCount <= 1024)
        {
            _precompiledGraph.Dispose();
        }
        _precompiledVectorGraph.Dispose();

        for (var i = 0; i < _gpuScalars.Length; i++)
        {
            if (_gpuScalars[i].Value.DevicePointer != IntPtr.Zero) 
                _gpuScalars[i].Dispose();
        }

        _gpuVector.Dispose();

        _gpuConstant.Dispose();
        _context.Dispose();
        _scope.Dispose();
    }

    private GpuBigInt CreateGpuBigInt(BigInteger val, int numWords)
    {
        var isNeg = val < 0;
        if (isNeg)
        {
            val = -val;
        }

        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));

        var u = new GpuBigUInt(_context, buffer);
        return new GpuBigInt(u, isNeg);
    }

    // =========================================================================
    // Scenario 1: ZKP/FHE Polynomial / Multi-Scalar Accumulation (Sum(s_i * c))
    // =========================================================================

    [Benchmark(Baseline = true)]
    public BigInteger Cpu_PolynomialInnerProduct_PLINQ()
    {
#pragma warning disable RS0030 // Benchmark comparison against System.Linq / PLINQ
        return System.Linq.ParallelEnumerable.Aggregate(
            System.Linq.ParallelEnumerable.Select(
                System.Linq.ParallelEnumerable.AsParallel(_cpuScalars),
                s => s * _cpuConstant),
            BigInteger.Zero,
            (acc, cur) => acc + cur);
#pragma warning restore RS0030
    }

    [Benchmark]
    public GpuBigInt Gpu_SequentialEvaluate_PolynomialInnerProduct()
    {
        return _gpuScalars.AsStackEnumerable()
            .Select(s => s * _gpuConstant)
            .AsParallel()
            .Sum()
            .Evaluate(_context);
    }

    [Benchmark]
    public GpuBigInt Gpu_CudaGraph_PolynomialInnerProduct()
    {
        return _gpuScalars.AsStackEnumerable()
            .Select(s => s * _gpuConstant)
            .AsParallel()
            .Sum()
            .EvaluateWithCudaGraph(_context);
    }

    [Benchmark]
    public GpuBigInt Gpu_CudaGraph_Precompiled_PolynomialInnerProduct()
    {
        _precompiledGraph.Execute();
        return _precompiledGraph.Result;
    }

    [Benchmark]
    public GpuBigInt Gpu_CudaGraph_Vectorized_PolynomialInnerProduct()
    {
        _precompiledVectorGraph.Execute();
        return _precompiledVectorGraph.Result;
    }

    // =========================================================================
    // Direct CLI Runner for Instant Visual Feedback
    // =========================================================================

    public static void RunDirectBenchmark(int elementCount = 256, int numWords = 8, int iterations = 10)
    {
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");
        Logger<GpuBigIntLinqBenchmark>.Info(" Henicos GpuBigInt + StackLinq + CUDA Graph Benchmark (ZKP/MSM Scenario)");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");
        Logger<GpuBigIntLinqBenchmark>.Info($"Elements    : {elementCount:N0}");
        Logger<GpuBigIntLinqBenchmark>.Info($"Word Width  : {numWords} words ({numWords * 32} bits)");
        Logger<GpuBigIntLinqBenchmark>.Info($"Iterations  : {iterations}");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");

        using var bench = new GpuBigIntLinqBenchmark
        {
            ElementCount = elementCount,
            NumWords = numWords
        };
        bench.Setup();

        // 1. Warm-up
        Logger<GpuBigIntLinqBenchmark>.Info("Warming up engines...");
        _ = bench.Cpu_PolynomialInnerProduct_PLINQ();
        if (elementCount <= 1024)
        {
            using var warmSeq = bench.Gpu_SequentialEvaluate_PolynomialInnerProduct();
            using var warmGraph = bench.Gpu_CudaGraph_PolynomialInnerProduct();
            _ = bench.Gpu_CudaGraph_Precompiled_PolynomialInnerProduct();
        }
        _ = bench.Gpu_CudaGraph_Vectorized_PolynomialInnerProduct();

        // 2. CPU PLINQ Measurement
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = bench.Cpu_PolynomialInnerProduct_PLINQ();
        }

        sw.Stop();
        var cpuElapsedMs = (double)sw.ElapsedMilliseconds / iterations;

        // 3. GPU Sequential Evaluate Measurement (Skip if too large to prevent driver OOM/Timeout)
        double gpuSeqElapsedMs = 0;
        if (elementCount <= 1024)
        {
            sw.Restart();
            for (var i = 0; i < iterations; i++)
            {
                using var r = bench.Gpu_SequentialEvaluate_PolynomialInnerProduct();
            }

            sw.Stop();
            gpuSeqElapsedMs = (double)sw.ElapsedMilliseconds / iterations;
        }

        // 4. GPU CUDA Graph Measurement (Skip if too large)
        double gpuGraphElapsedMs = 0;
        if (elementCount <= 1024)
        {
            sw.Restart();
            for (var i = 0; i < iterations; i++)
            {
                using var r = bench.Gpu_CudaGraph_PolynomialInnerProduct();
            }

            sw.Stop();
            gpuGraphElapsedMs = (double)sw.ElapsedMilliseconds / iterations;
        }

        // 5. GPU CUDA Graph Precompiled Measurement (Skip if too large)
        double gpuPrecompElapsedMs = 0;
        if (elementCount <= 1024)
        {
            sw.Restart();
            for (var i = 0; i < iterations; i++)
            {
                _ = bench.Gpu_CudaGraph_Precompiled_PolynomialInnerProduct();
            }

            sw.Stop();
            gpuPrecompElapsedMs = (double)sw.ElapsedMilliseconds / iterations;
        }

        // 6. GPU CUDA Graph Vectorized Measurement
        sw.Restart();
        for (var i = 0; i < iterations; i++)
        {
            _ = bench.Gpu_CudaGraph_Vectorized_PolynomialInnerProduct();
        }

        sw.Stop();
        var gpuVectorElapsedMs = (double)sw.ElapsedMilliseconds / iterations;

        // 7. Output formatted results table
        Logger<GpuBigIntLinqBenchmark>.Info("");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "| Engine / Mode                      | Latency (ms) | Speedup vs CPU | Graph Launch Gain |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "|:-----------------------------------|-------------:|---------------:|------------------:|");
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| CPU (System.Numerics + PLINQ)      | {cpuElapsedMs,10:F2} ms |          1.00x |                 - |");

        if (elementCount <= 1024)
        {
            Logger<GpuBigIntLinqBenchmark>.Info(
                $"| GPU (GpuBigInt Sequential Evaluate)| {gpuSeqElapsedMs,10:F2} ms | {cpuElapsedMs / Math.Max(0.001, gpuSeqElapsedMs),13:F2}x |             1.00x |");
            Logger<GpuBigIntLinqBenchmark>.Info(
                $"| GPU (GpuBigInt + CUDA Graph)       | {gpuGraphElapsedMs,10:F2} ms | {cpuElapsedMs / Math.Max(0.001, gpuGraphElapsedMs),13:F2}x | {gpuSeqElapsedMs / Math.Max(0.001, gpuGraphElapsedMs),16:F2}x |");
            Logger<GpuBigIntLinqBenchmark>.Info(
                $"| GPU (CUDA Graph Precompiled)       | {gpuPrecompElapsedMs,10:F2} ms | {cpuElapsedMs / Math.Max(0.001, gpuPrecompElapsedMs),13:F2}x | {gpuSeqElapsedMs / Math.Max(0.001, gpuPrecompElapsedMs),16:F2}x |");
            Logger<GpuBigIntLinqBenchmark>.Info(
                $"| GPU (Vectorized + Precompiled Graph)| {gpuVectorElapsedMs,9:F2} ms | {cpuElapsedMs / Math.Max(0.001, gpuVectorElapsedMs),13:F2}x | {gpuSeqElapsedMs / Math.Max(0.001, gpuVectorElapsedMs),16:F2}x |");
        }
        else
        {
            Logger<GpuBigIntLinqBenchmark>.Info(
                $"| GPU (Vectorized + Precompiled Graph)| {gpuVectorElapsedMs,9:F2} ms | {cpuElapsedMs / Math.Max(0.001, gpuVectorElapsedMs),13:F2}x |     (Skipped) |");
        }
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");
    }
}
