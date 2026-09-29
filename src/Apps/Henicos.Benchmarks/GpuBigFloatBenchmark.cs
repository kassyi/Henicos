using Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Henicos.Benchmarks;

public class GpuBigFloatBenchmark : GpuBigMathBenchmarkBase
{
    private GpuUIntBigInteger[] _leftSigs = null!;
    private GpuUIntBigInteger[] _rightSigs = null!;
    private GpuUIntBigInteger[] _resultSigs = null!;

    protected override void OnSetup()
    {
        _leftSigs = new GpuUIntBigInteger[BatchSize];
        _rightSigs = new GpuUIntBigInteger[BatchSize];
        _resultSigs = new GpuUIntBigInteger[BatchSize];

        var rnd = new Random(42);
        var leftData = new uint[NumWords];
        var rightData = new uint[NumWords];
        for (var i = 0; i < NumWords; i++)
        {
            leftData[i] = (uint)rnd.Next();
            rightData[i] = (uint)rnd.Next();
        }

        leftData[^1] |= 0x80000000;
        rightData[^1] |= 0x80000000;

        for (var i = 0; i < BatchSize; i++)
        {
            _leftSigs[i] = new GpuUIntBigInteger(Ctx, NumWords);
            _rightSigs[i] = new GpuUIntBigInteger(Ctx, NumWords);
            _resultSigs[i] = new GpuUIntBigInteger(Ctx, NumWords);

            Ctx.Provider.CopyHostToDevice(_leftSigs[i].DevicePointer, leftData, NumWords, Stream);
            Ctx.Provider.CopyHostToDevice(_rightSigs[i].DevicePointer, rightData, NumWords, Stream);
        }
    }

    protected override void OnCleanup()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            _leftSigs[i].Dispose();
            _rightSigs[i].Dispose();
            _resultSigs[i].Dispose();
        }
    }

    protected override void ExecuteBatchMultiply()
    {
        var execCtx = new BigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        for (var i = 0; i < BatchSize; i++)
        {
            GSharpAotFloatProvider.Instance.Multiply(ref execCtx, _leftSigs[i].Handle, 1000, false,
                _rightSigs[i].Handle, 1000, false, _resultSigs[i].Handle, out _, out _);
        }
    }

    protected override void ExecuteBatchDivide()
    {
        var execCtx = new BigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        for (var i = 0; i < BatchSize; i++)
        {
            GSharpAotFloatProvider.Instance.Divide(ref execCtx, _leftSigs[i].Handle, 1000, false, _rightSigs[i].Handle,
                1000, false, _resultSigs[i].Handle, out _, out _);
        }
    }

    protected override void ExecuteBatchSqrt()
    {
        var execCtx = new BigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        for (var i = 0; i < BatchSize; i++)
        {
            GSharpAotFloatProvider.Instance.Sqrt(ref execCtx, _leftSigs[i].Handle, 1000, false, _resultSigs[i].Handle,
                out _, out _);
        }
    }
}
