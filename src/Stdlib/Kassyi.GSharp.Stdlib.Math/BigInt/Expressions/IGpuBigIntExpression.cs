namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

/// <summary>
/// Represents a signed GPU BigInteger AST node.
/// </summary>
public interface IGpuBigIntExpression
{
    /// <summary>
    /// Gets the number of words (32-bit units) required to store the absolute magnitude of this expression.
    /// </summary>
    int NumWords
    {
        get;
    }

    /// <summary>
    /// Evaluates this AST node into a new or provided GPU buffer, returning a signed <see cref="GpuBigInteger"/>.
    /// </summary>
    /// <param name="context">The GPU context for evaluation.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the absolute magnitude. If null, a temporary buffer is rented.</param>
    /// <returns>The evaluated result with its sign.</returns>
    GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null);
}
