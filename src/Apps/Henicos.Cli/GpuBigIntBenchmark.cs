using Kassyi.GSharp.Runtime.Aot;

namespace Henicos.Cli;

/// <summary>BenchmarkDotNet performance comparisons between legacy native and GSharp GPU kernel execution providers.</summary>
[MarkdownExporterAttribute.GitHub]
public class GpuBigIntBenchmark : IDisposable
{
    [Params(1024, 4096, 16384)]
    public int NumWords
    {
        get; set;
    }

    private GpuWorkspace? _contextKernel;

    private GpuBigUInt _gpuAKernel;
    private GpuBigUInt _gpuBKernel;
    private GpuBigUInt _gpuResultKernel;

    private GpuKernelLoader.CudaContextScope _scope;

    [GlobalSetup]
    public void Setup()
    {
        _scope = new GpuKernelLoader.CudaContextScope();

        _contextKernel = new GpuWorkspace(NumWords * 2);

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

        _gpuAKernel = new GpuBigUInt(_contextKernel, hostA);
        _gpuBKernel = new GpuBigUInt(_contextKernel, hostB);
        _gpuResultKernel = new GpuBigUInt(_contextKernel, NumWords);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _contextKernel?.Dispose();
        _scope.Dispose();
    }

    [Benchmark]
    public void Sub_GSharpKernel() => _gpuResultKernel.Value = _gpuAKernel - _gpuBKernel;

    public void Dispose() => Cleanup();
}

