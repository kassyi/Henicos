namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Provides a zero-allocation, forward-only stack enumerator over a managed array.
/// </summary>
/// <typeparam name="T">The type of elements in the array.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Guarantees zero heap allocation ($O(1)$ space) during enumeration.</para>
/// </remarks>
public struct ArrayEnumerator<T> : IStackEnumerator<T>
{
    private readonly T[] _source;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayEnumerator{T}"/> struct wrapping the specified array.
    /// </summary>
    /// <param name="source">The source array to enumerate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public ArrayEnumerator(T[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _index = 0;
    }

    /// <summary>
    /// Attempts to advance the enumerator to the next element of the array.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the element at the current index;
    /// otherwise, the default value of <typeparamref name="T"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator successfully advanced to the next element;
    /// <see langword="false"/> if the end of the array was reached.
    /// </returns>
    public bool TryGetNext(out T current)
    {
        if ((uint)_index < (uint)_source.Length)
        {
            current = _source[_index++];
            return true;
        }

        // Safe fallback: default value when enumeration has reached the end
        current = default!;
        return false;
    }
}
