
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorWhereExpression(
    [TakesOwnership] IGpuBigIntVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigIntVectorExpression
{
    public int Length => source.Length;
    public int WordsPerElement => source.WordsPerElement;

    private GpuBigUIntArray _compactedValues;
    private GpuBigUIntArray _compactedSigns;
    private GpuBigUIntArray _scanResult;
    private bool _allocated;

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            throw new NotSupportedException(
                "Where expression does not support targetResult because its size is dynamic.");
        }

        var evaluatedSource = source.Evaluate(workspace);
        var evaluatedPredicates = predicate.Evaluate(workspace);

        var originalCount = evaluatedSource.Length;
        if (originalCount == 0)
        {
            return VectorResultCache.EmptySigned(workspace, WordsPerElement);
        }

        if (!_allocated)
        {
            _scanResult = new GpuBigUIntArray(workspace, originalCount, 1);
            _compactedValues = new GpuBigUIntArray(workspace, originalCount, WordsPerElement);
            _compactedSigns = new GpuBigUIntArray(workspace, originalCount, 1);
            _allocated = true;
        }

        // 1. Exclusive Scan on Predicates
        StreamCompactionOrchestrator.ExecuteExclusiveScan(
            workspace, _scanResult.DevicePointer, evaluatedPredicates.DevicePointer, originalCount);

        // 2. Scatter (Compact) Data
        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedValues.DevicePointer,
                evaluatedSource.Values.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                WordsPerElement));

        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedSigns.DevicePointer,
                evaluatedSource.Signs.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                1));

        var finalCount = elementCount ?? (originalCount - elementOffset);
        return new GpuBigIntArray(
            _compactedValues.BorrowSlice(elementOffset, finalCount),
            _compactedSigns.BorrowSlice(elementOffset, finalCount));
    }

    public void Dispose()
    {
        source.Dispose();
        predicate.Dispose();
        if (!_allocated)
        {
            return;
        }

        _scanResult.Dispose();
        _compactedValues.Dispose();
        _compactedSigns.Dispose();
    }
}
