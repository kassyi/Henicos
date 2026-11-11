namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The two vector operands of a node, evaluated over the same window. When both read one shared sub-expression at
/// different offsets (<c>e.Skip(1).ZipSubtract(e.Take(3))</c>), the right one evaluates that node again into the
/// buffers that hold the left one's result; the right result is then kept in this pair's cache and the left one
/// evaluated again.
/// </summary>
/// <remarks>
/// A cache lends its buffers from their start whatever the window, so the words of the two results cannot tell
/// whether they hold the same window; a node read twice at the same offset is also evaluated again.
/// </remarks>
internal sealed class VectorOperandPair : IDisposable
{
    // A right result kept here while the left one is evaluated again; it lends nothing otherwise
    private readonly VectorResultCache _kept = new();

    /// <summary>
    /// After <paramref name="left"/> and then the right input were evaluated over the same window: when the right
    /// evaluation overwrote the left result, keeps the right result here and evaluates <paramref name="left"/> again,
    /// so that both results are intact.
    /// </summary>
    public void KeepBoth(
        IGpuBigIntVectorExpression left,
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        ref VectorEvaluation leftResult,
        ref VectorEvaluation rightResult)
    {
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
