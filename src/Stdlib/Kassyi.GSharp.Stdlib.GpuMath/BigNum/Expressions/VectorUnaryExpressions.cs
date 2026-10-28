namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class VectorUnaryExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
    public int WordsPerElement => vector.WordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept (design 4.6)
    private readonly VectorResultCache _cache = new();

    public GpuBigIntArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        ValidateBeforeEvaluate();

        var evalCount = elementCount ?? (Length - elementOffset);
        // The target is checked before the children run, so that a rejected target evaluates nothing
        GpuBigUIntArray? targetValues = targetResult is { } target
            ? VectorResultCache.TargetValues(target, evalCount, WordsPerElement)
            : null;

        var vecResult = vector.Evaluate(workspace, elementOffset, evalCount);
        var destination = _cache.Signed(workspace, evalCount, WordsPerElement, targetValues);
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
        _cache.Dispose();
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
