namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an expression node that evaluates to a contiguous array of GPU big integers.
/// </summary>
public interface IGpuBigIntVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Gets the number of 32-bit words per element.
    /// </summary>
    int WordsPerElement { get; }

    /// <summary>
    /// Evaluates the vectorized expression into a new or existing device array, optionally computing only a sub-slice.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate. If null, evaluates to the end of the vector length.</param>
    /// <param name="targetResult">
    /// An optional destination for the evaluated values, with the expression's <see cref="WordsPerElement"/> and at least
    /// <paramref name="elementCount"/> elements; only the first <paramref name="elementCount"/> are written. The default
    /// view means none (an array of 0 elements is a target); a <see cref="GpuBigUIntArray"/> converts to a view implicitly. The expression borrows it
    /// and never frees it.
    /// </param>
    /// <returns>
    /// A borrowed view of exactly the evaluated length: the node's own result buffers (with the target's values if one
    /// was given) or the input array. It is valid until the next evaluation or until the expression is disposed; pass a
    /// target to keep the values. The next evaluation overwrites the values of a result still held, without an error.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetResult"/> has another number of words per element, or fewer elements than evaluated.
    /// </exception>
    GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default);
}
