using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math;

namespace Henicos.Benchmarks;

[Config(typeof(GpuBenchmarkConfig))]
[MemoryDiagnoser]
public abstract class GpuBigMathBenchmarkBase : IDisposable
{
    protected class GpuBenchmarkConfig : ManualConfig
    {
        public GpuBenchmarkConfig()
        {
            // GPUベンチマークにおけるウォームアップと反復の厳密制御
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(5)      // GPU JIT / PTXコンパイルの完全排除
                .WithIterationCount(15)   // 測定の標準偏差を抑え込む
                .AsDefault());
            
            AddColumn(new BitsColumn());
            AddColumn(new OpsPerSecColumn());
        }
    }

    protected GpuKernelLoader.CudaContextScope _scope;
    protected GpuWorkspace _ctx = null!;
    protected IntPtr _stream;

    // 精度（ビット幅）のパラメータ
    [Params(2048)]
    public int NumWords { get; set; } // 65,536 bits

    // バッチサイズのパラメータ（現在は1要素ずつの直列処理になるため、レイテンシ評価の 1 のみ）
    [Params(1)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scope = new GpuKernelLoader.CudaContextScope();
        _ctx = new GpuWorkspace(16);
        
        _stream = _ctx.Stream;

        // 1. 全バッファを事前に確保（Pinned Memory / Device Memory Arena）
        // 2. テスト入力データの初期化とGPUへの一括転送
        // 3. CUDA Event の作成
        OnSetup();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        OnCleanup();

        _ctx.Dispose();
        _scope.Dispose();
    }

    protected abstract void OnSetup();
    protected abstract void OnCleanup();

    [Benchmark(Baseline = true)]
    public void Multiply()
    {
        // ホスト-デバイス間の同期を明示的に行い、非同期完了抜けを防止
        ExecuteBatchMultiply();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_stream));
    }

    [Benchmark]
    public void Divide()
    {
        ExecuteBatchDivide();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_stream));
    }

    [Benchmark]
    public void Sqrt()
    {
        ExecuteBatchSqrt();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_stream));
    }

    protected abstract void ExecuteBatchMultiply();
    protected abstract void ExecuteBatchDivide();
    protected abstract void ExecuteBatchSqrt();
}
