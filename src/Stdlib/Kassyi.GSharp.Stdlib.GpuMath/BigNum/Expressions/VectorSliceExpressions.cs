namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorTakeExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    public int Length =>
        TryGetLength(out var length) ? length : throw new InvalidOperationException(DeviceCounted.LengthUnknown);

    public int Capacity => Math.Min(source.Capacity, count);

    public int WordsPerElement => source.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = source.TryGetLength(out var sourceLength);
        length = known ? Math.Min(sourceLength, count) : 0;
        return known;
    }

    public VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        var evalCount = elementCount ?? (Capacity - elementOffset);

        // Ensure we do not evaluate beyond the Take boundary; a device count below it is cut by the source's TakeFirst
        var effectiveCount = Math.Min(evalCount, Capacity - elementOffset);

        if (effectiveCount <= 0)
        {
            return new VectorEvaluation(
                VectorSlice.Empty(workspace, WordsPerElement, targetResult), DeviceElementCount.Static(0));
        }

        return DeviceCounted.Evaluate(source, workspace, elementOffset, effectiveCount, targetResult);
    }

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

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
    : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    // Skipping past the capacity skips everything; a device count below the skip gives 0 through SkipFirst
    private readonly int _skipCount = Math.Min(count, source.Capacity);

    public int Length =>
        TryGetLength(out var length) ? length : throw new InvalidOperationException(DeviceCounted.LengthUnknown);

    public int Capacity => source.Capacity - _skipCount;

    public int WordsPerElement => source.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = source.TryGetLength(out var sourceLength);
        length = known ? sourceLength - _skipCount : 0;
        return known;
    }

    public VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        var evalCount = elementCount ?? (Capacity - elementOffset);

        if (evalCount <= 0)
        {
            return new VectorEvaluation(
                VectorSlice.Empty(workspace, WordsPerElement, targetResult), DeviceElementCount.Static(0));
        }

        // Pass the requested element slice but shifted by the Skip count
        return DeviceCounted.Evaluate(source, workspace, elementOffset + _skipCount, evalCount, targetResult);
    }

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    public void Dispose() => source.Dispose();
}
