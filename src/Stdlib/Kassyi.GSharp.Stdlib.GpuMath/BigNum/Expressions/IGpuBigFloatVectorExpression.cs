namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an expression node that evaluates to a contiguous array of GPU arbitrary-precision floating-point numbers.
/// </summary>
public interface IGpuBigFloatVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The number is known only on the device (the expression contains a <c>Where</c>); use <see cref="Capacity"/>,
    /// <see cref="TryGetLength"/> or <c>Count()</c>.
    /// </exception>
    int Length { get; }

    /// <summary>
    /// Gets the upper bound of the number of elements, known before evaluation: the <see cref="Length"/> when it is
    /// known on the host, otherwise the length of the input of the <c>Where</c>.
    /// </summary>
    int Capacity => Length;

    /// <summary>
    /// Gets the number of elements when it is known on the host without evaluating, as
    /// <c>Enumerable.TryGetNonEnumeratedCount</c> does.
    /// </summary>
    /// <param name="length">The number of elements, or 0 when it is known only on the device.</param>
    /// <returns><see langword="true"/> when the number is known on the host.</returns>
    bool TryGetLength(out int length)
    {
        length = Length;
        return true;
    }

    /// <summary>
    /// Gets the number of 32-bit significand words per element.
    /// </summary>
    int WordsPerElement { get; }

    /// <summary>
    /// Evaluates the vectorized floating-point expression into a new or existing device array, optionally computing only a sub-slice.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate. If null, evaluates to the end of the vector length.</param>
    /// <param name="targetResult">
    /// An optional destination with the expression's <see cref="WordsPerElement"/> and at least
    /// <paramref name="elementCount"/> elements; only the first <paramref name="elementCount"/> are written. The default
    /// view means none (an array of 0 elements is a target); a <see cref="GpuBigFloatArray"/> converts to a view implicitly. The expression borrows it
    /// and never frees it. It may share memory with an input (the input array itself, or a shifted view of it): the node
    /// then computes into its own result buffers and copies the result into the target.
    /// </param>
    /// <returns>
    /// A borrowed view of exactly the evaluated length: the node's own result buffers, the target, or the input array. It
    /// is valid until the next evaluation or until the expression is disposed; pass a target to keep the values. The next
    /// evaluation overwrites the values of a result still held, without an error.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetResult"/> has another number of words per element, or fewer elements than evaluated.
    /// </exception>
    GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default);
}
