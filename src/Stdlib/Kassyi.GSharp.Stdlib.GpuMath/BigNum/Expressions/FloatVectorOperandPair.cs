namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>The floating-point form of <see cref="VectorOperandPair"/>.</summary>
internal sealed class FloatVectorOperandPair : IDisposable
{
    private readonly VectorResultCache _kept = new();

    /// <inheritdoc cref="VectorOperandPair.Evaluate"/>
    public void Evaluate(
        IGpuBigFloatVectorExpression left,
        IGpuBigFloatVectorExpression right,
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        out FloatVectorEvaluation leftResult,
        out FloatVectorEvaluation rightResult)
    {
        leftResult = DeviceCounted.Evaluate(left, workspace, elementOffset, elementCount);
        rightResult = DeviceCounted.Evaluate(right, workspace, elementOffset, elementCount);
        if (leftResult.Lease.IsCurrent)
        {
            return;
        }

        var kept = rightResult.Storage;
        var copy = _kept.Float(workspace, kept.Length, kept.WordsPerElement);
        VectorResultCache.Deliver(workspace, kept, copy);
        rightResult = new FloatVectorEvaluation(copy, rightResult.Count);
        leftResult = DeviceCounted.Evaluate(left, workspace, elementOffset, elementCount);
    }

    /// <inheritdoc cref="VectorOperandPair.Dispose"/>
    public void Dispose() => _kept.Dispose();
}
