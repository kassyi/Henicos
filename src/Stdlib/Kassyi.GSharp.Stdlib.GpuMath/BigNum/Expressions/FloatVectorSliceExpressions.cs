namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression
{
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Length);

    public int Length => inner.Length - _effectiveSkip;
    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default)
    {
        var count = elementCount ?? (Length - elementOffset);
        if (count <= 0)
        {
            return FloatVectorSlice.Empty(workspace, WordsPerElement, targetResult);
        }

        return inner.Evaluate(workspace, _effectiveSkip + elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}

internal static class FloatVectorSlice
{
    // An evaluation of no elements writes nothing, but a target of another width is still rejected, as by every node
    public static GpuBigFloatArrayView Empty(GpuWorkspace workspace, int wordsPerElement, GpuBigFloatArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            VectorResultCache.TargetFloat(targetResult, 0, wordsPerElement);
        }

        return VectorResultCache.EmptyFloat(workspace, wordsPerElement);
    }
}

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression
{
    public int Length { get; } = Math.Clamp(takeCount, 0, inner.Length);

    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        var effectiveCount = Math.Min(evalCount, Length - elementOffset);
        if (effectiveCount <= 0)
        {
            return FloatVectorSlice.Empty(workspace, WordsPerElement, targetResult);
        }

        return inner.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => inner.Dispose();
}
