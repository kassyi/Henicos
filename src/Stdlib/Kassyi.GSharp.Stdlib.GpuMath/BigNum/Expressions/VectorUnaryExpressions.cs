namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class VectorUnaryExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
    public int WordsPerElement => vector.WordsPerElement;

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        ValidateBeforeEvaluate();

        var evalCount = elementCount ?? (Length - elementOffset);
        var vecResult = vector.Evaluate(workspace, elementOffset, evalCount);

        if (targetResult != null)
        {
            _finalValues = targetResult.Value;
            if (!_allocated)
            {
                _finalSigns = new GpuBigUIntArray(workspace, evalCount, 1);
                _allocated = true;
            }
        }
        else if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, evalCount, WordsPerElement);
            _finalSigns = new GpuBigUIntArray(workspace, evalCount, 1);
            _allocated = true;
        }

        var destination = new GpuBigIntArray(_finalValues, _finalSigns);
        if (evalCount > 0)
        {
            EvaluateElements(workspace, vecResult, destination, evalCount);
        }

        return destination;
    }

    protected virtual void ValidateBeforeEvaluate()
    {
    }

    protected abstract void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArray source,
        GpuBigIntArray destination,
        int evalCount);

    public void Dispose()
    {
        if (_allocated)
        {
            if (_finalValues.Handle.Buffer != null)
            {
                _finalValues.Dispose();
            }

            _finalSigns.Dispose();
            _allocated = false;
        }

        vector.Dispose();
    }
}

internal sealed class VectorSqrtExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : VectorUnaryExpressionBase(vector)
{
    protected override void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArray source,
        GpuBigIntArray destination,
        int evalCount)
    {
        var srcSigns = ArrayPool<uint>.Shared.Rent(evalCount);
        try
        {
            workspace.Provider.CopyDeviceToHost(
                srcSigns, source.Signs.DevicePointer, evalCount, workspace.Stream);

            for (var i = 0; i < evalCount; i++)
            {
                var srcElem = source.Values.GetElement(i);
                if (srcSigns[i] != 0 && !srcElem.IsZero())
                {
                    throw new InvalidOperationException("Cannot compute square root of a negative integer.");
                }

                var dstElem = destination.Values.GetElement(i);
                GpuBigIntDivRem.Sqrt(srcElem, dstElem);
            }

            workspace.Provider.ClearMemoryAsync(destination.Signs.DevicePointer, evalCount, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcSigns);
        }
    }
}

internal sealed class VectorDivByUInt32Expression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    uint divisor) : VectorUnaryExpressionBase(vector)
{
    protected override void ValidateBeforeEvaluate()
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }
    }

    protected override void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArray source,
        GpuBigIntArray destination,
        int evalCount)
    {
        var srcSigns = ArrayPool<uint>.Shared.Rent(evalCount);
        var dstSigns = ArrayPool<uint>.Shared.Rent(evalCount);
        try
        {
            workspace.Provider.CopyDeviceToHost(
                srcSigns, source.Signs.DevicePointer, evalCount, workspace.Stream);

            for (var i = 0; i < evalCount; i++)
            {
                var srcElem = source.Values.GetElement(i);
                var dstElem = destination.Values.GetElement(i);
                GpuBigIntMath.DivideByUInt32(srcElem, divisor, dstElem);

                dstSigns[i] = (srcSigns[i] != 0 && !dstElem.IsZero()) ? 1u : 0u;
            }

            workspace.Provider.CopyHostToDevice(
                destination.Signs.DevicePointer, dstSigns, evalCount, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcSigns);
            ArrayPool<uint>.Shared.Return(dstSigns);
        }
    }
}
