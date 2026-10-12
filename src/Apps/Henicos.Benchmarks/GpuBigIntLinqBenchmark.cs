using System.Linq;
using System.Numerics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

#nullable disable

namespace Henicos.Benchmarks;

/// <summary>
/// Comprehensive Benchmark Suite for LINQ to GPU operators across CPU/GPU and execution modes.
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

    // GPU Precompiled Graphs
    private GpuBigIntExecutableGraph _precompiledMapGraph;
    private GpuBigIntExecutableGraph _precompiledFilterGraph;
    private GpuBigIntExecutableGraph _precompiledPipelineGraph;
    private GpuBigIntExecutableGraph _precompiledVectorMapGraph;
    private GpuBigIntExecutableGraph _precompiledVectorFilterGraph;
    private GpuBigIntExecutableGraph _precompiledVectorPipelineGraph;

    private GpuBigIntArray _gpuVector;

    [Params(256, 1024, 65536)]
    public int ElementCount { get; set; } = 256;

    [Params(8)] // 256-bit
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
            rawBytes[^1] &= 0x7F;

            var val = new BigInteger(rawBytes);
            _cpuScalars[i] = val;

            var valBytes = val.ToByteArray();
            var uSpan = MemoryMarshal.Cast<byte, uint>(valBytes);
            for (int j = 0; j < uSpan.Length && j < NumWords; j++)
            {
                flatValues[i * NumWords + j] = uSpan[j];
            }

            flatSigns[i] = val.Sign < 0 ? 1u : 0u;
        }

        _gpuVector = new GpuBigIntArray(_context, ElementCount, NumWords);
        _context.Provider.CopyHostToDevice(_gpuVector.Values.DevicePointer, flatValues, flatValues.Length,
            _context.Stream);
        _context.Provider.CopyHostToDevice(_gpuVector.Signs.DevicePointer, flatSigns, flatSigns.Length,
            _context.Stream);

        if (ElementCount <= 1024)
        {
            for (var i = 0; i < ElementCount; i++)
            {
                _gpuScalars[i] = CreateGpuBigInt(_cpuScalars[i], NumWords);
            }

            _precompiledMapGraph = _gpuScalars.AsStackEnumerable()
                .Select(s => s * _gpuConstant)
                .AsParallel()
                .Sum()
                .CompileToGraph(_context);

            _precompiledFilterGraph = _gpuScalars.AsStackEnumerable()
                .Where(s => s > _gpuConstant)
                .AsParallel()
                .Sum()
                .CompileToGraph(_context);

            _precompiledPipelineGraph = _gpuScalars.AsStackEnumerable()
                .Where(s => s > _gpuConstant)
                .Select(s => s * _gpuConstant)
                .AsParallel()
                .Sum()
                .CompileToGraph(_context);
        }

        _precompiledVectorMapGraph = _gpuVector.AsVectorizedEnumerable()
            .SelectMultiply(_gpuConstant)
            .Sum()
            .CompileToGraph(_context);

        _precompiledVectorFilterGraph = _gpuVector.AsVectorizedEnumerable()
            .Where(_gpuVector.AsVectorizedEnumerable().IsGreaterThan(_gpuConstant))
            .Sum()
            .CompileToGraph(_context);

        _precompiledVectorPipelineGraph = _gpuVector.AsVectorizedEnumerable()
            .Where(_gpuVector.AsVectorizedEnumerable().IsGreaterThan(_gpuConstant))
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
            _precompiledMapGraph.Dispose();
            _precompiledFilterGraph.Dispose();
            _precompiledPipelineGraph.Dispose();
        }

        _precompiledVectorMapGraph.Dispose();
        _precompiledVectorFilterGraph.Dispose();
        _precompiledVectorPipelineGraph.Dispose();

        for (var i = 0; i < _gpuScalars.Length; i++)
        {
            if (_gpuScalars[i].Value.DevicePointer != IntPtr.Zero)
            {
                _gpuScalars[i].Dispose();
            }
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

    // --- CPU Baselines ---
    [Benchmark(Baseline = true)]
    public BigInteger Cpu_MapReduce_PLINQ()
    {
#pragma warning disable RS0030
        return ParallelEnumerable.Aggregate(
            ParallelEnumerable.Select(
                ParallelEnumerable.AsParallel(_cpuScalars),
                s => s * _cpuConstant),
            BigInteger.Zero,
            (acc, cur) => acc + cur);
#pragma warning restore RS0030
    }

    public BigInteger Cpu_FilterReduce_PLINQ()
    {
#pragma warning disable RS0030
        return ParallelEnumerable.Aggregate(
            ParallelEnumerable.Where(
                ParallelEnumerable.AsParallel(_cpuScalars),
                s => s > _cpuConstant),
            BigInteger.Zero,
            (acc, cur) => acc + cur);
#pragma warning restore RS0030
    }

    public BigInteger Cpu_Pipeline_PLINQ()
    {
#pragma warning disable RS0030
        return ParallelEnumerable.Aggregate(
            ParallelEnumerable.Select(
                ParallelEnumerable.Where(
                    ParallelEnumerable.AsParallel(_cpuScalars),
                    s => s > _cpuConstant),
                s => s * _cpuConstant),
            BigInteger.Zero,
            (acc, cur) => acc + cur);
#pragma warning restore RS0030
    }

    // --- GPU Executions ---
    public void Gpu_VectorMap_Precompiled() => _precompiledVectorMapGraph.Execute();
    public void Gpu_VectorFilter_Precompiled() => _precompiledVectorFilterGraph.Execute();
    public void Gpu_VectorPipeline_Precompiled() => _precompiledVectorPipelineGraph.Execute();
    public void Gpu_Filter_Precompiled() => _precompiledFilterGraph.Execute();
    public void Gpu_Pipeline_Precompiled() => _precompiledPipelineGraph.Execute();
    public void Gpu_StackMap_Precompiled() => _precompiledMapGraph.Execute();

    public static void RunDirectBenchmark(int elementCount = 256, int numWords = 8, int iterations = 10)
    {
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");
        Logger<GpuBigIntLinqBenchmark>.Info(" Henicos LINQ to GPU Comprehensive Benchmark Suite");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");
        Logger<GpuBigIntLinqBenchmark>.Info($"Elements    : {elementCount:N0}");
        Logger<GpuBigIntLinqBenchmark>.Info($"Word Width  : {numWords} words ({numWords * 32} bits)");
        Logger<GpuBigIntLinqBenchmark>.Info($"Iterations  : {iterations}");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "================================================================================");

        using var bench = new GpuBigIntLinqBenchmark { ElementCount = elementCount, NumWords = numWords };
        bench.Setup();

        Logger<GpuBigIntLinqBenchmark>.Info("Warming up...");
        _ = bench.Cpu_MapReduce_PLINQ();
        _ = bench.Cpu_FilterReduce_PLINQ();
        _ = bench.Cpu_Pipeline_PLINQ();
        bench.Gpu_VectorMap_Precompiled();
        bench.Gpu_VectorFilter_Precompiled();
        bench.Gpu_VectorPipeline_Precompiled();

        if (elementCount <= 1024)
        {
            bench.Gpu_StackMap_Precompiled();
            bench.Gpu_Filter_Precompiled();
            bench.Gpu_Pipeline_Precompiled();
        }

        var sw = new Stopwatch();

        var cpuMapMs = measure(() => _ = bench.Cpu_MapReduce_PLINQ());
        var cpuFilterMs = measure(() => _ = bench.Cpu_FilterReduce_PLINQ());
        var cpuPipelineMs = measure(() => _ = bench.Cpu_Pipeline_PLINQ());

        var gpuVecMapMs = measure(bench.Gpu_VectorMap_Precompiled);
        var gpuVecFilterMs = measure(bench.Gpu_VectorFilter_Precompiled);
        var gpuVecPipelineMs = measure(bench.Gpu_VectorPipeline_Precompiled);

        double gpuStackMapMs = 0, gpuFilterMs = 0, gpuPipelineMs = 0;
        if (elementCount <= 1024)
        {
            gpuStackMapMs = measure(bench.Gpu_StackMap_Precompiled);
            gpuFilterMs = measure(bench.Gpu_Filter_Precompiled);
            gpuPipelineMs = measure(bench.Gpu_Pipeline_Precompiled);
        }

        Logger<GpuBigIntLinqBenchmark>.Info("");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "| Scenario / Mode                     | Latency (ms) | Speedup vs CPU | M Elements / s |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            "|:------------------------------------|-------------:|---------------:|---------------:|");

        // Scenario 1: MapReduce
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| [Map] CPU (Numerics + PLINQ)        | {cpuMapMs,10:F2} ms |          1.00x | {formatThroughput(cpuMapMs)} |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| [Map] GPU (Vectorized CUDA Graph)   | {gpuVecMapMs,10:F2} ms | {formatSpeedup(cpuMapMs, gpuVecMapMs)} | {formatThroughput(gpuVecMapMs)} |");
        if (elementCount <= 1024)
        {
            Logger<GpuBigIntLinqBenchmark>.Info(
                $"| [Map] GPU (Stack CUDA Graph)        | {gpuStackMapMs,10:F2} ms | {formatSpeedup(cpuMapMs, gpuStackMapMs)} | {formatThroughput(gpuStackMapMs)} |");
        }

        // Scenario 2: FilterReduce
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| [Filter] CPU (Numerics + PLINQ)     | {cpuFilterMs,10:F2} ms |          1.00x | {formatThroughput(cpuFilterMs)} |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| [Filter] GPU (Vectorized CUDA Graph)| {gpuVecFilterMs,10:F2} ms | {formatSpeedup(cpuFilterMs, gpuVecFilterMs)} | {formatThroughput(gpuVecFilterMs)} |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            elementCount <= 1024
                ? $"| [Filter] GPU (Stack CUDA Graph)     | {gpuFilterMs,10:F2} ms | {formatSpeedup(cpuFilterMs, gpuFilterMs)} | {formatThroughput(gpuFilterMs)} |"
                : "| [Filter] GPU (Stack CUDA Graph)     |          N/A |            N/A | (Limit: 1024)  |");

        // Scenario 3: Pipeline (Filter + Map + Reduce)
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| [Pipeline] CPU (Numerics + PLINQ)   | {cpuPipelineMs,10:F2} ms |          1.00x | {formatThroughput(cpuPipelineMs)} |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            $"| [Pipeline] GPU (Vectorized CUDA Graph)| {gpuVecPipelineMs,10:F2} ms | {formatSpeedup(cpuPipelineMs, gpuVecPipelineMs)} | {formatThroughput(gpuVecPipelineMs)} |");
        Logger<GpuBigIntLinqBenchmark>.Info(
            elementCount <= 1024
                ? $"| [Pipeline] GPU (Stack CUDA Graph)   | {gpuPipelineMs,10:F2} ms | {formatSpeedup(cpuPipelineMs, gpuPipelineMs)} | {formatThroughput(gpuPipelineMs)} |"
                : "| [Pipeline] GPU (Stack CUDA Graph)   |          N/A |            N/A | (Limit: 1024)  |");

        Logger<GpuBigIntLinqBenchmark>.Info(
            "==================================================================================");
        return;

        string formatSpeedup(double cpuMs, double gpuMs) =>
            gpuMs > 0 ? $"{cpuMs / Math.Max(0.001, gpuMs),13:F2}x" : "          N/A";

        double calcThroughput(double ms) => ms > 0 ? elementCount / ms / 1000.0 : 0;

        string formatThroughput(double ms) => ms > 0 ? $"{calcThroughput(ms),11:F2} M/s" : "        N/A";

        double measure(Action action)
        {
            sw.Restart();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            sw.Stop();
            return (double)sw.ElapsedMilliseconds / iterations;
        }
    }
}
