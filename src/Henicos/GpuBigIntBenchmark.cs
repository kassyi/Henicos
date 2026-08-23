using BenchmarkDotNet.Attributes;
using Kassyi.GSharp.GpuBigInt;
using Kassyi.GSharp.Runtime.Jit;

namespace Henicos;

[MarkdownExporterAttribute.GitHub]
public class GpuBigIntBenchmark : IDisposable
{
    [Params(1024, 4096, 16384)]
    public int NumWords
    {
        get; set;
    }

    private GpuContext _contextJit = null!;
    private GpuContext _contextLegacy = null!;

    private GpuBigInteger _gpuAJit = null!;
    private GpuBigInteger _gpuBJit = null!;
    private GpuBigInteger _gpuResultJit = null!;

    private GpuBigInteger _gpuALegacy = null!;
    private GpuBigInteger _gpuBLegacy = null!;
    private GpuBigInteger _gpuResultLegacy = null!;

    private JitEngine.CudaContextScope _scope;

    [GlobalSetup]
    public void Setup()
    {
        _scope = new JitEngine.CudaContextScope();

        _contextJit = new GpuContext(NumWords * 2);
        _contextLegacy = new GpuContext(NumWords * 2, GpuExecutionEngine.LegacyNative);

        var rnd = new Random(42);
        var hostA = new uint[NumWords];
        var hostB = new uint[NumWords];
        for (int i = 0; i < NumWords; i++)
        {
            hostA[i] = (uint)rnd.Next() | ((uint)rnd.Next() << 16);
            hostB[i] = (uint)rnd.Next() | ((uint)rnd.Next() << 16);
        }

        // Ensure A >= B
        hostA[NumWords - 1] |= 0x40000000;
        hostB[NumWords - 1] &= 0x3FFFFFFF;

        _gpuAJit = new GpuBigInteger(_contextJit, hostA);
        _gpuBJit = new GpuBigInteger(_contextJit, hostB);
        _gpuResultJit = new GpuBigInteger(_contextJit, NumWords);

        _gpuALegacy = new GpuBigInteger(_contextLegacy, hostA);
        _gpuBLegacy = new GpuBigInteger(_contextLegacy, hostB);
        _gpuResultLegacy = new GpuBigInteger(_contextLegacy, NumWords);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _gpuAJit.Dispose();
        _gpuBJit.Dispose();
        _gpuResultJit.Dispose();

        _gpuALegacy.Dispose();
        _gpuBLegacy.Dispose();
        _gpuResultLegacy.Dispose();

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
    public void Sub_GSharpJit() => _gpuResultJit.Value = _gpuAJit - _gpuBJit;

    public void Dispose() => Cleanup();
}
