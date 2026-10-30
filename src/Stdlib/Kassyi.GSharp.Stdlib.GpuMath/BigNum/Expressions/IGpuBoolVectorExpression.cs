namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Represents a deferred GPU vector expression yielding an array of boolean predicates.
/// </summary>
public interface IGpuBoolVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector expression.
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
    /// Evaluates the deferred boolean vector expression on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace memory context.</param>
    /// <returns>
    /// A borrowed view containing 1 for true and 0 for false, valid until the next evaluation or until the expression is
    /// disposed. The next evaluation overwrites the values of a result still held, without an error.
    /// </returns>
    GpuBigUIntArrayView Evaluate(GpuWorkspace workspace);
}
