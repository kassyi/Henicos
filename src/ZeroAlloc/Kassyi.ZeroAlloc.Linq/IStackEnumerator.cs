namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Defines a generalized, zero-allocation, forward-only stack enumerator interface.
/// </summary>
/// <typeparam name="T">The type of objects to enumerate.</typeparam>
/// <remarks>
/// <para><b>Allocation:</b> Designed to be implemented by <see langword="ref struct"/> or value type <see langword="struct"/> types to guarantee zero heap allocations during iteration.</para>
/// <para><b>Thread Safety:</b> Enumerators are mutable stateful value types and are not thread-safe.</para>
/// </remarks>
public interface IStackEnumerator<T>
{
    /// <summary>
    /// Attempts to advance the enumerator to the next element of the sequence and outputs the current element if available.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the next element in the sequence;
    /// otherwise, the default value of <typeparamref name="T"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator was successfully advanced to the next element;
    /// <see langword="false"/> if the enumerator has passed the end of the sequence.
    /// </returns>
    bool TryGetNext([MaybeNullWhen(false)] out T current);
}
