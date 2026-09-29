using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Cli;

/// <summary>BenchmarkDotNet performance comparisons between legacy native and GSharp JIT execution providers.</summary>
[MarkdownExporterAttribute.GitHub]
public class GpuBigIntBenchmark : IDisposable
{
    [Params(1024, 4096, 16384)]
    public int NumWords
    {
        get; set;
    }

    private GpuWorkspace _contextJit = null!;
    private GpuWorkspace _contextLegacy = null!;

    private GpuUIntBigInteger _gpuAJit;
    private GpuUIntBigInteger _gpuBJit;
    private GpuUIntBigInteger _gpuResultJit;

    private GpuUIntBigInteger _gpuALegacy;
    private GpuUIntBigInteger _gpuBLegacy;
    private GpuUIntBigInteger _gpuResultLegacy;

    private GpuKernelLoader.CudaContextScope _scope;

    [GlobalSetup]
    public void Setup()
    {
        _scope = new GpuKernelLoader.CudaContextScope();

        _contextJit = new GpuWorkspace(NumWords * 2);
        _contextLegacy = new GpuWorkspace(NumWords * 2, GpuExecutionEngine.LegacyNative);

        var rnd = new Random(42);
        var hostA = new uint[NumWords];
        var hostB = new uint[NumWords];
        for (var i = 0; i < NumWords; i++)
        {
            hostA[i] = (uint)rnd.Next() | ((uint)rnd.Next() << 16);
            hostB[i] = (uint)rnd.Next() | ((uint)rnd.Next() << 16);
        }

        // Ensure A >= B
        hostA[NumWords - 1] |= 0x40000000;
        hostB[NumWords - 1] &= 0x3FFFFFFF;

        _gpuAJit = new GpuUIntBigInteger(_contextJit, hostA);
        _gpuBJit = new GpuUIntBigInteger(_contextJit, hostB);
        _gpuResultJit = new GpuUIntBigInteger(_contextJit, NumWords);

        _gpuALegacy = new GpuUIntBigInteger(_contextLegacy, hostA);
        _gpuBLegacy = new GpuUIntBigInteger(_contextLegacy, hostB);
        _gpuResultLegacy = new GpuUIntBigInteger(_contextLegacy, NumWords);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _contextJit.Dispose();
        _contextLegacy.Dispose();
        _scope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Sub_LegacyNative()
    {
        _gpuResultLegacy.Value = _gpuALegacy - _gpuBLegacy;
        // Synchronize implicitly or explicitly depending on how benchmarking is done.
        // For accurate GPU benchmarking, we should sync, but NativeMethods currently sync internally via cudaDeviceSynchronize.
    }

    [Benchmark]
    public void Sub_GSharpAot() => _gpuResultJit.Value = _gpuAJit - _gpuBJit;

    public void Dispose() => Cleanup();
}

