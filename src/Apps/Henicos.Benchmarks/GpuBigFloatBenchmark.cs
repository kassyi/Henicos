using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;


namespace Henicos.Benchmarks;

public class GpuBigFloatBenchmark : GpuBigMathBenchmarkBase
{
    private GpuBigUInt[] _leftSigs = [];
    private GpuBigUInt[] _rightSigs = [];
    private GpuBigUInt[] _resultSigs = [];

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
        var provider = GSharpAotFloatProvider.CreateGSharpKernel();
        for (var i = 0; i < BatchSize; i++)
        {
            var leftOp = new GpuFloatOperand(_leftSigs[i].Handle, 1000);
            var rightOp = new GpuFloatOperand(_rightSigs[i].Handle, 1000);
            provider.Multiply(ref execCtx, in leftOp, in rightOp, _resultSigs[i].Handle);
        }
    }

    protected override void ExecuteBatchDivide()
    {
        var execCtx = new GpuBigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        var provider = GSharpAotFloatProvider.CreateGSharpKernel();
        for (var i = 0; i < BatchSize; i++)
        {
            var leftOp = new GpuFloatOperand(_leftSigs[i].Handle, 1000);
            var rightOp = new GpuFloatOperand(_rightSigs[i].Handle, 1000);
            provider.Divide(ref execCtx, in leftOp, in rightOp, _resultSigs[i].Handle);
        }
    }

    protected override void ExecuteBatchSqrt()
    {
        var execCtx = new GpuBigIntExecutionContext(Ctx, Ctx.MemoryManager, Stream);
        var provider = GSharpAotFloatProvider.CreateGSharpKernel();
        for (var i = 0; i < BatchSize; i++)
        {
            var leftOp = new GpuFloatOperand(_leftSigs[i].Handle, 1000);
            provider.Sqrt(ref execCtx, in leftOp, _resultSigs[i].Handle);
        }
    }
}
