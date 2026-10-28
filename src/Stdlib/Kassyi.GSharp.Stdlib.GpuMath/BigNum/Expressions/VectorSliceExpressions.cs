namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorTakeExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression
{
    public int Length => Math.Min(source.Length, count);
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default)
    {
        var evalCount = elementCount ?? (Length - elementOffset);

        // Ensure we do not evaluate beyond the Take boundary
        var effectiveCount = Math.Min(evalCount, Length - elementOffset);

        if (effectiveCount <= 0)
        {
            return VectorSlice.Empty(workspace, WordsPerElement, targetResult);
        }

        return source.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => source.Dispose();
}

internal static class VectorSlice
{
    // An evaluation of no elements writes nothing, but a target of another width is still rejected, as by every node
    public static GpuBigIntArrayView Empty(GpuWorkspace workspace, int wordsPerElement, GpuBigUIntArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            VectorResultCache.TargetValues(targetResult, 0, wordsPerElement);
        }

        return VectorResultCache.EmptySigned(workspace, wordsPerElement);
    }
}

internal class VectorSkipExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression
{
    private readonly int _skipCount = Math.Min(count, source.Length);

    public int Length => source.Length - _skipCount;
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default)
    {
        var evalCount = elementCount ?? (Length - elementOffset);

        if (evalCount <= 0)
        {
            return VectorSlice.Empty(workspace, WordsPerElement, targetResult);
        }

        // Pass the requested element slice but shifted by the Skip count
        return source.Evaluate(workspace, elementOffset + _skipCount, evalCount, targetResult);
    }

    public void Dispose() => source.Dispose();
}
