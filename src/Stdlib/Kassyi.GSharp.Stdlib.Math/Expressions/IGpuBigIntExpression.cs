
namespace Kassyi.GSharp.Stdlib.Math.Expressions;

/// <summary>
/// Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated GPU BigInteger expression.
/// </summary>
public interface IGpuBigIntExpression
{
    /// <summary>
    /// Gets the word length (limb count) resulting from this expression.
    /// </summary>
    int NumWords
    {
        get;
    }

    /// <summary>
    /// Evaluates this expression on the GPU into the specified target buffer.
    /// </summary>
    /// <param name="context">The active GPU execution context.</param>
    /// <param name="targetResult">Optional target buffer to write the evaluated result into.</param>
    /// <returns>The evaluated <see cref="GpuBigInteger"/> result buffer.</returns>
    GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null);
}

