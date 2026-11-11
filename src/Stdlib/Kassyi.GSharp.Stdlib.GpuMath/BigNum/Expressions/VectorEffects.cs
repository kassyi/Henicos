namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Whether the evaluation of a vector expression can throw: a zero divisor, the square root of a negative value, inputs
/// of different lengths. <c>Count</c> and <c>Any</c> may answer from the length alone only when it cannot, because
/// <c>System.Linq</c> runs every projection that a <c>Count</c> enumerates. Each node decides it once, when it is
/// built, from its operation and its inputs, so that an expression of plain operations costs nothing more to count.
/// </summary>
internal static class VectorEffects
{
    /// <summary>Gets whether evaluating <paramref name="vector"/> can throw.</summary>
    /// <param name="vector">
    /// An <see cref="IGpuBigIntVectorExpression"/>, <see cref="IGpuBigFloatVectorExpression"/> or
    /// <see cref="IGpuBoolVectorExpression"/>.
    /// </param>
    /// <returns>
    /// The decision of a GpuMath node; <see langword="false"/> for an array; <see langword="true"/> for an expression
    /// implemented outside GpuMath, whose evaluation alone can tell.
    /// </returns>
    public static bool MayThrow(object vector) => vector switch
    {
        IVectorOperator op => op.MayThrow,
        GpuBigIntVectorLeafExpression or GpuBigFloatVectorLeafExpression => false,
        _ => true
    };

    /// <summary>Gets whether evaluating the scalar operand of a vector node can throw.</summary>
    /// <param name="scalar">The scalar operand.</param>
    /// <returns><see langword="false"/> for a value; <see langword="true"/> for a compound scalar expression.</returns>
    public static bool MayThrow(IGpuBigIntExpression scalar) => scalar is not GpuBigIntLeafExpression;
}
