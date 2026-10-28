namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Represents a deferred GPU vector expression yielding an array of boolean predicates.
/// </summary>
public interface IGpuBoolVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector expression.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Evaluates the deferred boolean vector expression on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace memory context.</param>
    /// <returns>
    /// A borrowed view containing 1 for true and 0 for false, valid until the next evaluation or until the expression is
    /// disposed. The next evaluation overwrites the values of a result still held, without an error.
    /// </returns>
    GpuBigUIntArrayView Evaluate(GpuWorkspace workspace);
}
