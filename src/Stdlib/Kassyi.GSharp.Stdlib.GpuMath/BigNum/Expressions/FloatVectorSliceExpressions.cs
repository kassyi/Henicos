namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression
{
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Length);

    public int Length => inner.Length - _effectiveSkip;
    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        if (count <= 0)
        {
            return new GpuBigFloatArray(workspace, 0, WordsPerElement);
        }

        return inner.Evaluate(workspace, _effectiveSkip + elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression
{
    public int Length { get; } = Math.Clamp(takeCount, 0, inner.Length);

    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        var effectiveCount = Math.Min(evalCount, Length - elementOffset);
        if (effectiveCount <= 0)
        {
            return new GpuBigFloatArray(workspace, 0, WordsPerElement);
        }

        return inner.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => inner.Dispose();
}
