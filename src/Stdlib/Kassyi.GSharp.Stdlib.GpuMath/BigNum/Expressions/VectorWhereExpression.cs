
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

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        if (targetResult != null)
        {
            throw new NotSupportedException(
                "Where expression does not support targetResult because its size is dynamic.");
        }

        var evaluatedSource = source.Evaluate(workspace);
        var evaluatedPredicates = predicate.Evaluate(workspace);

        var originalCount = evaluatedSource.Length;
        if (originalCount == 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
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
            _compactedValues.Slice(elementOffset, finalCount),
            _compactedSigns.Slice(elementOffset, finalCount));
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
