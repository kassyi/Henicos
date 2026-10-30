namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class VectorUnaryExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    public int Length => vector.Length;
    public int WordsPerElement => vector.WordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept (design 4.6)
    private readonly VectorResultCache _cache = new();

    public int Capacity => vector.Capacity;

    public bool TryGetLength(out int length) => vector.TryGetLength(out length);

    // Evaluates the window of the capacity; the elements are the first Count of them (design D3 4.3)
    public VectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        ValidateBeforeEvaluate();

        var evalCount = elementCount ?? (Capacity - elementOffset);
        // The target is checked before the children run, so that a rejected target evaluates nothing
        var targetValues = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetValues(targetResult, evalCount, WordsPerElement)
            : default;

        var vecResult = DeviceCounted.Evaluate(vector, workspace, elementOffset, evalCount);
        // A target that shares words with the input is written from the cache after the loop: the loop writes element i
        // before it reads element i + 1 (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, targetValues, vecResult.Storage);

        // The elements run on the host one by one: only the elements, not the rest of the capacity, so that a value
        // after the count cannot throw (a negative square root). A device count costs one synchronization here.
        var elements = vecResult.Count.IsStatic ? evalCount : vecResult.Count.Read(workspace);
        if (elements > 0)
        {
            EvaluateElements(workspace, vecResult.Storage, output.Destination, elements);
        }

        return new VectorEvaluation(output.Close(workspace), vecResult.Count);
    }

    protected virtual void ValidateBeforeEvaluate()
    {
    }

    protected abstract void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
        int evalCount);

    public void Dispose()
    {
        // A cache release that throws (a failed capture refuses the clear) does not skip the children
        try
        {
            _cache.Dispose();
        }
        finally
        {
            vector.Dispose();
        }
    }
}

internal sealed class VectorSqrtExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : VectorUnaryExpressionBase(vector)
{
    protected override void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
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
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
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
