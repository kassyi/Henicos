
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorWhereExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigFloatVectorExpression
{
    public int Length => source.Length;
    public int WordsPerElement => source.WordsPerElement;

    private GpuBigUIntArray _compactedSignificands;
    private GpuBigUIntArray _compactedExponents;
    private GpuBigUIntArray _compactedSigns;
    private GpuBigUIntArray _scanResult;
    private bool _allocated;

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            throw new NotSupportedException("Where expression does not support targetResult because its size is dynamic.");
        }

        var evaluatedSource = source.Evaluate(workspace);
        var evaluatedPredicates = predicate.Evaluate(workspace);

        var originalCount = evaluatedSource.Length;
        if (originalCount == 0)
        {
            return VectorResultCache.EmptyFloat(workspace, WordsPerElement);
        }

        if (!_allocated)
        {
            _scanResult = new GpuBigUIntArray(workspace, originalCount, 1);
            _compactedSignificands = new GpuBigUIntArray(workspace, originalCount, WordsPerElement);
            _compactedExponents = new GpuBigUIntArray(workspace, originalCount, 1);
            _compactedSigns = new GpuBigUIntArray(workspace, originalCount, 1);
            _allocated = true;
        }

        StreamCompactionOrchestrator.ExecuteExclusiveScan(
            workspace, _scanResult.DevicePointer, evaluatedPredicates.DevicePointer, originalCount);

        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedSignificands.DevicePointer,
                evaluatedSource.Significands.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                WordsPerElement));

        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedExponents.DevicePointer,
                evaluatedSource.Exponents.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                1));

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
        return new GpuBigFloatArray(
            _compactedSignificands.BorrowSlice(elementOffset, finalCount),
            _compactedExponents.BorrowSlice(elementOffset, finalCount),
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
        _compactedSignificands.Dispose();
        _compactedExponents.Dispose();
        _compactedSigns.Dispose();
    }
}
