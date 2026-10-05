using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

namespace Henicos.Benchmarks;

public class GpuBigFloatBenchmark : GpuBigMathBenchmarkBase
{
    private GpuBigUInt[] _leftSigs = null!;
    private GpuBigUInt[] _rightSigs = null!;
    private GpuBigUInt[] _resultSigs = null!;

    protected override void OnSetup()
    {
        _leftSigs = new GpuBigUInt[BatchSize];
        _rightSigs = new GpuBigUInt[BatchSize];
        _resultSigs = new GpuBigUInt[BatchSize];

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
            _leftSigs[i] = new GpuBigUInt(Ctx, NumWords);
            _rightSigs[i] = new GpuBigUInt(Ctx, NumWords);
            _resultSigs[i] = new GpuBigUInt(Ctx, NumWords);

            Ctx.Provider.CopyHostToDevice(_leftSigs[i].DevicePointer, leftData, NumWords, Stream);
            Ctx.Provider.CopyHostToDevice(_rightSigs[i].DevicePointer, rightData, NumWords, Stream);
        }
    }

    protected override void ExecuteBatchMultiply()
    {
        var execCtx = new GpuBigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        for (var i = 0; i < BatchSize; i++)
        {
            GSharpAotFloatProvider.CreateGSharpKernel().Multiply(ref execCtx, _leftSigs[i].Handle, 1000, false,
                _rightSigs[i].Handle, 1000, false, _resultSigs[i].Handle, out _, out _);
        }
    }

    protected override void ExecuteBatchDivide()
    {
        var execCtx = new GpuBigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        for (var i = 0; i < BatchSize; i++)
        {
            GSharpAotFloatProvider.CreateGSharpKernel().Divide(ref execCtx, _leftSigs[i].Handle, 1000, false,
                _rightSigs[i].Handle,
                1000, false, _resultSigs[i].Handle, out _, out _);
        }
    }

    protected override void ExecuteBatchSqrt()
    {
        var execCtx = new GpuBigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        for (var i = 0; i < BatchSize; i++)
        {
            GSharpAotFloatProvider.CreateGSharpKernel().Sqrt(ref execCtx, _leftSigs[i].Handle, 1000, false,
                _resultSigs[i].Handle,
                out _, out _);
        }
    }
}
