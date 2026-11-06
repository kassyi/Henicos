namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Whether the result of a node is still in the node's cache: a node evaluated again lends the same buffers and
/// overwrites its earlier result. The default lease (a leaf, an empty result, a node outside GpuMath) never ends.
/// </summary>
/// <param name="cache">The cache that lent the result, or <see langword="null"/>.</param>
/// <param name="generation">The generation of <paramref name="cache"/> when the result was handed up.</param>
internal readonly struct VectorLease(VectorResultCache? cache, int generation)
{
    /// <summary>Gets whether the cache has not lent its buffers again since the result was handed up.</summary>
    public bool IsCurrent => cache is null || cache.IsCurrent(generation);
}

/// <summary>
/// The two vector operands of a node, evaluated over the same window. When both read one shared sub-expression at
/// different offsets (<c>e.Skip(1).ZipSubtract(e.Take(3))</c>), the right one evaluates that node again into the buffers
/// that hold the left one's result; the right result is then kept in this pair's cache and the left one evaluated
/// again.
/// </summary>
/// <remarks>
/// A cache lends its buffers from their start whatever the window, so the words of the two results cannot tell whether
/// they hold the same window; a node read twice at the same offset is also evaluated again.
/// </remarks>
internal sealed class VectorOperandPair : IDisposable
{
    // A right result kept here while the left one is evaluated again; it lends nothing otherwise
    private readonly VectorResultCache _kept = new();

    /// <summary>Evaluates <paramref name="left"/>, then <paramref name="right"/>, each with its result intact.</summary>
    public void Evaluate(
        IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right,
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        out VectorEvaluation leftResult,
        out VectorEvaluation rightResult)
    {
        leftResult = DeviceCounted.Evaluate(left, workspace, elementOffset, elementCount);
        rightResult = DeviceCounted.Evaluate(right, workspace, elementOffset, elementCount);
        if (leftResult.Lease.IsCurrent)
        {
            return;
        }

        var kept = rightResult.Storage;
        var values = _kept.Values(workspace, kept.Length, kept.WordsPerElement);
        var signs = _kept.Signs(workspace, kept.Length);
        VectorResultCache.CopyValues(workspace, values, kept.Values);
        VectorResultCache.CopyValues(workspace, signs, kept.Signs);
        rightResult = new VectorEvaluation(new GpuBigIntArrayView(values, signs), rightResult.Count);
        leftResult = DeviceCounted.Evaluate(left, workspace, elementOffset, elementCount);
    }

    /// <summary>Releases the kept result; the operands belong to the node.</summary>
    public void Dispose() => _kept.Dispose();
}

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
