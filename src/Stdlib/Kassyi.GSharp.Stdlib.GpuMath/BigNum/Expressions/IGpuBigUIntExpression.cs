namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using Runtime;

/// <summary>
/// Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated GPU BigInteger expression.
/// </summary>
public interface IGpuBigUIntExpression
{
    /// <summary>
    /// Gets the word length (limb count) resulting from this expression.
    /// </summary>
    int NumWords
    {
        get;
    }

    /// <summary>
    /// Accepts a visitor to evaluate the expression tree using a scoped memory context.
    /// </summary>
    ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
}
