namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>Implemented by the GpuMath floating-point vector nodes, as <see cref="IDeviceCountedVector"/>.</summary>
internal interface IDeviceCountedFloatVector : IGpuBigFloatVectorExpression
{
    /// <summary>
    /// Evaluates elements <paramref name="elementOffset"/> to <c>elementOffset + elementCount</c> of the capacity;
    /// the count says how many of them are elements.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="elementOffset">The starting element index.</param>
    /// <param name="elementCount">The number of capacity elements to evaluate, or <see langword="null"/> for all remaining elements.</param>
    /// <param name="targetResult">An optional destination buffer for the evaluated elements.</param>
    /// <returns>The evaluated floating-point elements and their device element count.</returns>
    FloatVectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigFloatArrayView targetResult);

    /// <inheritdoc />
    GpuBigFloatArrayView IGpuBigFloatVectorExpression.Evaluate(GpuWorkspace workspace, int elementOffset,
        int? elementCount, GpuBigFloatArrayView targetResult) =>
        DeviceCounted.Materialize(workspace,
            DeviceCounted.Evaluate(this, workspace, elementOffset, elementCount, targetResult));
}
