namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Provides batched/vectorized LINQ operations for <see cref="GpuBigIntArray"/>.
/// </summary>
public static class GpuBigIntVectorLinqExtensions
{
    /// <summary>
    /// Converts a <see cref="GpuBigIntArray"/> into a vectorized deferred expression.
    /// </summary>
    /// <param name="source">The source <see cref="GpuBigIntArray"/>.</param>
    /// <returns>A vectorized expression wrapping the array.</returns>
    public static IGpuBigIntVectorExpression AsVectorizedEnumerable(this GpuBigIntArray source)
    {
        return new GpuBigIntVectorLeafExpression(source);
    }

    /// <summary>
    /// Multiplies each element of a vectorized expression by a scalar.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to multiply each element by.</param>
    /// <returns>A new vectorized expression representing the scalar multiplication.</returns>
    public static IGpuBigIntVectorExpression SelectMultiply(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar)
    {
        return new VectorMulScalarExpression(source, new GpuBigIntLeafExpression(scalar));
    }

    /// <summary>
    /// Computes the sum of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel sum reduction.</returns>
    public static GpuBigIntExpr Sum(this IGpuBigIntVectorExpression source)
    {
        return new GpuBigIntExpr(new VectorSumReductionExpression(source));
    }
}
