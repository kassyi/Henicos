namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using Runtime;

/// <summary>
/// Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated unsigned GPU BigInteger expression.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Implements the Visitor pattern (<see cref="BigIntExpressionVisitor"/>) to traverse expression trees
/// and emit fused or optimized GPU kernel sequences without intermediate managed object allocations.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> The expression evaluation returns a stack-scoped <see cref="ScopedBigIntMemory"/> handle.
/// </para>
/// </remarks>
public interface IGpuBigUIntExpression
{
    /// <summary>
    /// Gets the word length (32-bit limb count) resulting from this expression.
    /// </summary>
    int NumWords
    {
        get;
    }

    /// <summary>
    /// Accepts an expression visitor to evaluate the expression tree using a scoped device memory context.
    /// </summary>
    /// <param name="visitor">The expression visitor responsible for evaluating intermediate nodes.</param>
    /// <returns>A <see cref="ScopedBigIntMemory"/> instance holding the evaluated GPU memory slice.</returns>
    ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
}

