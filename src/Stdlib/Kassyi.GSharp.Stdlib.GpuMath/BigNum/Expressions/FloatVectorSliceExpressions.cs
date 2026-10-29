namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    // Skipping past the capacity skips everything; a device count below the skip gives 0 through SkipFirst
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Capacity);

    public int Length =>
        TryGetLength(out var length) ? length : throw new InvalidOperationException(DeviceCounted.LengthUnknown);
    public int Capacity => inner.Capacity - _effectiveSkip;
    public int WordsPerElement => inner.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = inner.TryGetLength(out var innerLength);
        length = known ? innerLength - _effectiveSkip : 0;
        return known;
    }

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        var count = elementCount ?? (Capacity - elementOffset);
        if (count <= 0)
        {
            return new FloatVectorEvaluation(
                FloatVectorSlice.Empty(workspace, WordsPerElement, targetResult), DeviceElementCount.Static(0));
        }

        return DeviceCounted.Evaluate(inner, workspace, _effectiveSkip + elementOffset, count, targetResult);
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
    int takeCount) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    public int Length =>
        TryGetLength(out var length) ? length : throw new InvalidOperationException(DeviceCounted.LengthUnknown);

    public int Capacity => Math.Clamp(takeCount, 0, inner.Capacity);

    public int WordsPerElement => inner.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        var known = inner.TryGetLength(out var innerLength);
        length = known ? Math.Clamp(takeCount, 0, innerLength) : 0;
        return known;
    }

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        var evalCount = elementCount ?? (Capacity - elementOffset);
        var effectiveCount = Math.Min(evalCount, Capacity - elementOffset);
        if (effectiveCount <= 0)
        {
            return new FloatVectorEvaluation(
                FloatVectorSlice.Empty(workspace, WordsPerElement, targetResult), DeviceElementCount.Static(0));
        }

        return DeviceCounted.Evaluate(inner, workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => inner.Dispose();
}
