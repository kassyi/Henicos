namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorTakeExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression
{
    public int Length => Math.Min(source.Length, count);
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);

        // Ensure we do not evaluate beyond the Take boundary
        var effectiveCount = Math.Min(evalCount, Length - elementOffset);

        if (effectiveCount <= 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }

        return source.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => source.Dispose();
}

internal class VectorSkipExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression
{
    private readonly int _skipCount = Math.Min(count, source.Length);

    public int Length => source.Length - _skipCount;
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);

        if (evalCount <= 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }

        // Pass the requested element slice but shifted by the Skip count
        return source.Evaluate(workspace, elementOffset + _skipCount, evalCount, targetResult);
    }

    public void Dispose() => source.Dispose();
}
