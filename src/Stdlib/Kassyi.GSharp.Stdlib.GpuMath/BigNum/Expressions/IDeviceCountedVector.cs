namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Implemented by the GpuMath vector nodes, which pass a device count to each other. A node writes only
/// <see cref="EvaluateCore"/>; its public <c>Evaluate</c> is that evaluation cut to the count.
/// </summary>
internal interface IDeviceCountedVector : IGpuBigIntVectorExpression
{
    /// <summary>
    /// Evaluates elements <paramref name="elementOffset"/> to <c>elementOffset + elementCount</c> of the capacity;
    /// the count says how many of them are elements.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="elementOffset">The starting element index.</param>
    /// <param name="elementCount">The number of capacity elements to evaluate, or <see langword="null"/> for all remaining elements.</param>
    /// <param name="targetResult">An optional destination buffer for the evaluated elements.</param>
    /// <returns>The evaluated elements and their device element count.</returns>
    VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult);

    /// <inheritdoc />
    GpuBigIntArrayView IGpuBigIntVectorExpression.Evaluate(GpuWorkspace workspace, int elementOffset,
        int? elementCount, GpuBigUIntArrayView targetResult) =>
        DeviceCounted.Materialize(workspace,
            DeviceCounted.Evaluate(this, workspace, elementOffset, elementCount, targetResult));
}
