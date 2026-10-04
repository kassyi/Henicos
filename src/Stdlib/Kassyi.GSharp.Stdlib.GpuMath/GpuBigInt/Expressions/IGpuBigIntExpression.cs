namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt.Expressions;

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
    /// Evaluates this AST node into a new or provided GPU buffer, returning a signed <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="workspace">The GPU workspace for evaluation.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the absolute magnitude. If null, a temporary buffer is rented.</param>
    /// <returns>The evaluated result with its sign.</returns>
    GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null);
}
