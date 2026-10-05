namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated signed GPU BigInteger expression.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Represents composite algebraic expressions containing signs and magnitudes, deferring kernel evaluation
/// until explicitly triggered via <see cref="Evaluate"/> to optimize scratch buffer reuse and fusion.
/// </para>
/// </remarks>
public interface IGpuBigIntExpression
{
    /// <summary>
    /// Gets the number of 32-bit words (limbs) required to store the absolute magnitude of this expression.
    /// </summary>
    int NumWords
    {
        get;
    }

    /// <summary>
    /// Evaluates this AST node into a newly rented or provided GPU buffer, returning a signed <see cref="GpuBigInt"/>.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing kernel execution and memory pools.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the absolute magnitude. If <see langword="null"/>, a temporary buffer is rented.</param>
    /// <returns>A <see cref="GpuBigInt"/> containing the evaluated magnitude and sign flag.</returns>
    GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null);
}

