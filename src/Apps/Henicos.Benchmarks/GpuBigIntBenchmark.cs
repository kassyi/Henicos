namespace Henicos.Benchmarks;

public class GpuBigIntBenchmark : GpuBigMathBenchmarkBase
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

    protected override void ExecuteBatchMultiply()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            Ctx.Provider.Mul(Ctx, _leftSigs[i], _rightSigs[i], _resultSigs[i]);
        }
    }

    protected override void ExecuteBatchDivide()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            GpuBigIntDivRem.Div(_leftSigs[i], _rightSigs[i], _resultSigs[i]);
        }
    }

    protected override void ExecuteBatchSqrt()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            GpuBigIntDivRem.Sqrt(_leftSigs[i], _resultSigs[i]);
        }
    }
}
