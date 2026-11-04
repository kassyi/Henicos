namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Provides a zero-allocation, forward-only stack enumerator over a <see cref="ReadOnlySpan{T}"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the span.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Operates directly over stack or contiguous memory with zero heap allocations.</para>
/// </remarks>
public ref struct SpanEnumerator<T> : IStackEnumerator<T>
{
    private readonly ReadOnlySpan<T> _source;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpanEnumerator{T}"/> struct wrapping the specified read-only span.
    /// </summary>
    /// <param name="source">The read-only span to enumerate.</param>
    public SpanEnumerator(ReadOnlySpan<T> source)
    {
        _source = source;
        _index = 0;
    }

    /// <summary>
    /// Attempts to advance the enumerator to the next element of the span.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the element at the current index;
    /// otherwise, the default value of <typeparamref name="T"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator successfully advanced to the next element;
    /// <see langword="false"/> if the end of the span was reached.
    /// </returns>
    public bool TryGetNext([MaybeNullWhen(false)] out T current)
    {
        if ((uint)_index < (uint)_source.Length)
        {
            current = _source[_index++];
            return true;
        }

        // Safe fallback: default value when enumeration has reached the end
        current = default;
        return false;
    }

    internal ReadOnlySpan<T> GetRemainingSpanAndAdvance()
    {
        var remaining = _source.Slice(_index);
        _index = _source.Length;
        return remaining;
    }
}
