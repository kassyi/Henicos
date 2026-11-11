namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an expression node that evaluates to a contiguous array of GPU big integers.
/// </summary>
/// <remarks>
/// <para>
/// The expressions of GpuMath read <see cref="Length"/>, <see cref="Capacity"/> and <see cref="TryGetLength"/> of their
/// inputs once, when they are built; an implementation of this interface must not change them once it is the input of
/// another expression.
/// </para>
/// <para>
/// <see cref="IDisposable.Dispose"/> of an expression of GpuMath releases it and every expression below it,
/// whatever the depth. A node that throws does not stop the others; one exception is rethrown as it is, several are
/// thrown together in an <see cref="AggregateException"/>.
/// </para>
/// </remarks>
public interface IGpuBigIntVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The number is known only on the device (the expression contains a <c>Where</c>); use <see cref="Capacity"/>,
    /// <see cref="TryGetLength"/> or <c>Count()</c>.
    /// </exception>
    /// <remarks>
    /// Must not change once the expression is the input of another expression, which reads it when it is built.
    /// </remarks>
    int Length { get; }

    /// <summary>
    /// Gets the upper bound of the number of elements, known before evaluation: the <see cref="Length"/> when it is
    /// known on the host, otherwise the length of the input of the <c>Where</c>.
    /// </summary>
    /// <remarks>
    /// Must not change once the expression is the input of another expression, which reads it when it is built.
    /// </remarks>
    int Capacity => Length;

    /// <summary>
    /// Gets the number of elements when it is known on the host without evaluating, as
    /// <c>Enumerable.TryGetNonEnumeratedCount</c> does.
    /// </summary>
    /// <param name="length">The number of elements, or 0 when it is known only on the device.</param>
    /// <returns><see langword="true"/> when the number is known on the host.</returns>
    /// <remarks>
    /// Must not change once the expression is the input of another expression, which reads it when it is built.
    /// </remarks>
    bool TryGetLength(out int length)
    {
        length = Length;
        return true;
    }

    /// <summary>
    /// Gets the number of 32-bit words per element.
    /// </summary>
    int WordsPerElement { get; }

    /// <summary>
    /// Gets an upper bound of every element's magnitude in bits; <see cref="WordsPerElement"/> holds it. The default
    /// is every bit of the words, for an expression that knows no tighter bound.
    /// </summary>
    /// <remarks>
    /// The library's expressions track their bounds in bits, so that a chain of additions grows by one bit per
    /// addition and by one word only every 32 additions.
    /// </remarks>
    long BitsPerElement => 32L * WordsPerElement;

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
    /// and never frees it. It may share memory with an input (the input array itself, or a shifted view of it): the node
    /// then computes into its own result buffers and copies the result into the target.
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
