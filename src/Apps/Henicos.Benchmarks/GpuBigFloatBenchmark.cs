using BenchmarkDotNet.Attributes;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Henicos.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class GpuBigFloatBenchmark : IDisposable
{
    private GpuKernelLoader.CudaContextScope _scope;
    private GpuContext _ctx = null!;
    
    private GpuUIntBigInteger _leftSig;
    private GpuUIntBigInteger _rightSig;
    private GpuUIntBigInteger _resultSig;

    [Params(2048, 4096)]
    public int NumWords { get; set; }

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable GSH0022
        _scope = new GpuKernelLoader.CudaContextScope();
        _ctx = new GpuContext(16);

        _leftSig = new GpuUIntBigInteger(_ctx, NumWords);
        _rightSig = new GpuUIntBigInteger(_ctx, NumWords);
        _resultSig = new GpuUIntBigInteger(_ctx, NumWords);
#pragma warning restore GSH0022

        var rnd = new Random(42);
        var leftData = new uint[NumWords];
        var rightData = new uint[NumWords];
        for (int i = 0; i < NumWords; i++)
        {
            leftData[i] = (uint)rnd.Next();
            rightData[i] = (uint)rnd.Next();
        }
        
        leftData[^1] |= 0x80000000;
        rightData[^1] |= 0x80000000;

        _ctx.Provider.CopyHostToDevice(_leftSig.DevicePointer, leftData, NumWords, _ctx.Stream);
        _ctx.Provider.CopyHostToDevice(_rightSig.DevicePointer, rightData, NumWords, _ctx.Stream);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
#pragma warning disable GSH0021
        _leftSig.Dispose();
        _rightSig.Dispose();
        _resultSig.Dispose();
        _ctx.Dispose();
        _scope.Dispose();
#pragma warning restore GSH0021
    }

    [Benchmark]
    public void Multiply()
    {
        var execCtx = new BigIntExecutionContext(_ctx, _ctx.MemoryManager, _ctx.Stream);
        CSharpBigFloatArithmetic.Instance.Multiply(ref execCtx, _leftSig.Handle, 1000, false, _rightSig.Handle, 1000, false, _resultSig.Handle, out _, out _);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_ctx.Stream));
    }

    [Benchmark]
    public void Divide()
    {
        var execCtx = new BigIntExecutionContext(_ctx, _ctx.MemoryManager, _ctx.Stream);
        CSharpBigFloatArithmetic.Instance.Divide(ref execCtx, _leftSig.Handle, 1000, false, _rightSig.Handle, 1000, false, _resultSig.Handle, out _, out _);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_ctx.Stream));
    }

    [Benchmark]
    public void Sqrt()
    {
        var execCtx = new BigIntExecutionContext(_ctx, _ctx.MemoryManager, _ctx.Stream);
        CSharpBigFloatArithmetic.Instance.Sqrt(ref execCtx, _leftSig.Handle, 1000, false, _resultSig.Handle, out _, out _);
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_ctx.Stream));
    }
}
