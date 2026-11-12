namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Opens a <see cref="VectorOutput{TKind, TResult, TTarget}"/> of an integer or a floating-point node.
/// </summary>
internal static class VectorOutput
{
    /// <summary>
    /// Opens the output of an integer node; see <see cref="VectorOutput{TKind, TResult, TTarget}.Open"/>.
    /// </summary>
    public static VectorOutput<IntVectorResultKind, GpuBigIntArrayView, GpuBigUIntArrayView> Open(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigUIntArrayView checkedTarget,
        GpuBigIntArrayView input,
        GpuBigIntArrayView other = default,
        GpuBigIntMemoryHandle scalar = default) =>
        VectorOutput<IntVectorResultKind, GpuBigIntArrayView, GpuBigUIntArrayView>.Open(
            cache,
            workspace,
            count,
            wordsPerElement,
            checkedTarget,
            input,
            other,
            scalar
        );

    /// <summary>
    /// Opens the output of a floating-point node; see <see cref="VectorOutput{TKind, TResult, TTarget}.Open"/>.
    /// </summary>
    public static VectorOutput<FloatVectorResultKind, GpuBigFloatArrayView, GpuBigFloatArrayView> Open(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigFloatArrayView checkedTarget,
        GpuBigFloatArrayView input,
        GpuBigFloatArrayView other = default,
        GpuBigIntMemoryHandle scalar = default) =>
        VectorOutput<FloatVectorResultKind, GpuBigFloatArrayView, GpuBigFloatArrayView>.Open(
            cache,
            workspace,
            count,
            wordsPerElement,
            checkedTarget,
            input,
            other,
            scalar
        );
}
