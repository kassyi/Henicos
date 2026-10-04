using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath;

namespace Henicos.Benchmarks;

[Config(typeof(GpuBenchmarkConfig))]
[MemoryDiagnoser]
public abstract class GpuBigMathBenchmarkBase : IDisposable
{
    protected class GpuBenchmarkConfig : ManualConfig
    {
        public GpuBenchmarkConfig()
        {
            // Strict control of warmup and iterations for GPU benchmarks
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(5)      // Ensure GPU JIT and PTX compilation overhead is completely eliminated
                .WithIterationCount(15)   // Minimize measurement standard deviation
                .AsDefault());

            AddColumn(new BitsColumn());
            AddColumn(new OpsPerSecColumn());
        }
    }

    protected GpuKernelLoader.CudaContextScope Scope;
    protected GpuWorkspace Ctx = null!;
    protected IntPtr Stream;

    // Precision parameter (word count / bit width)
    [Params(2048)]
    public int NumWords
    {
        get; set;
    } // 65,536 bits

    // Batch size parameter (currently 1 for latency evaluation under serial processing)
    [Params(1)]
    public int BatchSize
    {
        get; set;
    }

    [GlobalSetup]
    public void Setup()
    {
        Scope = new GpuKernelLoader.CudaContextScope();
        Ctx = new GpuWorkspace(16);

        Stream = Ctx.Stream;

        // 1. Pre-allocate all buffers (Pinned Memory / Device Memory Arena)
        // 2. Initialize test input data and perform bulk transfer to GPU
        // 3. Create CUDA Events
        OnSetup();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        OnCleanup();

        Ctx.Dispose();
        Scope.Dispose();
    }

    protected abstract void OnSetup();
    protected virtual void OnCleanup()
    {
    }

    [Benchmark(Baseline = true)]
    public void Multiply()
    {
        // Explicit host-device synchronization to ensure asynchronous kernel completion
        ExecuteBatchMultiply();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(Stream));
    }

    [Benchmark]
    public void Divide()
    {
        ExecuteBatchDivide();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(Stream));
    }

    [Benchmark]
    public void Sqrt()
    {
        ExecuteBatchSqrt();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(Stream));
    }

    protected abstract void ExecuteBatchMultiply();
    protected abstract void ExecuteBatchDivide();
    protected abstract void ExecuteBatchSqrt();
}
