namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The floating-point <c>Where</c> (design D3 4.4): the number of selected elements is computed on the device into a
/// word this node owns, as in <see cref="VectorWhereExpression"/>.
/// </summary>
internal sealed class FloatVectorWhereExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    public int Length => DeviceCounted.KnownLength(this);

    public int Capacity => source.Capacity;

    public int WordsPerElement => source.WordsPerElement;

    // The selected elements, the scan of the predicates, the count word and the block sums of the scan: owned here and
    // reused, so that an evaluation allocates nothing after the first
    private readonly WhereDeviceState _state = new();

    public bool TryGetLength(out int length)
    {
        length = 0;
        return false;
    }

    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        var capacity = Capacity;
        WhereDeviceState.ThrowIfCannotSelect(VectorResultCache.HasTarget(targetResult), capacity, predicate);
        if (capacity == 0)
        {
            return new FloatVectorEvaluation(VectorResultCache.EmptyFloat(workspace, WordsPerElement),
                DeviceElementCount.Static(0));
        }

        var evaluatedSource = DeviceCounted.Evaluate(source, workspace);
        var selected = _state.Select<FloatVectorResultKind, GpuBigFloatArrayView>(workspace, evaluatedSource.Storage,
            evaluatedSource.Count, predicate, capacity, WordsPerElement, elementOffset, elementCount, out var count);
        return new FloatVectorEvaluation(selected, count);
    }

    public void Dispose()
    {
        // A release that throws does not skip the others
        try
        {
            _state.Dispose();
        }
        finally
        {
            try
            {
                source.Dispose();
            }
            finally
            {
                predicate.Dispose();
            }
        }
    }
}
