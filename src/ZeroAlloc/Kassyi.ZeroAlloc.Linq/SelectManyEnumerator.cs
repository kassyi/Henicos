namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Projects each element of a sequence to an inner stack sequence and flattens the inner sequences into one, with zero
/// heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
/// <typeparam name="TInnerEnumerator">The type of the enumerator of each inner sequence.</typeparam>
/// <typeparam name="TResult">The type of the elements in the inner sequences.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Holds one inner enumerator at a time; $O(1)$ auxiliary space and zero heap allocations.</para>
/// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
/// </remarks>
public ref struct SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    where TInnerEnumerator : struct, IStackEnumerator<TResult>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, StackEnumerable<TInnerEnumerator, TResult>> _selector;
    private TInnerEnumerator _inner;
    private bool _hasInner;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="SelectManyEnumerator{TEnumerator, TSource, TInnerEnumerator, TResult}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="selector">A function that returns the inner sequence of a source element.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    public SelectManyEnumerator(TEnumerator source, Func<TSource, StackEnumerable<TInnerEnumerator, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _source = source;
        _selector = selector;
        _inner = default;
        _hasInner = false;
    }

    /// <summary>
    /// Attempts to advance to the next element of the current inner sequence, moving to the next source element when
    /// the inner sequence is exhausted.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the next inner element;
    /// otherwise, the default value of <typeparamref name="TResult"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an element was produced;
    /// <see langword="false"/> if the source and the last inner sequence are exhausted.
    /// </returns>
    public bool TryGetNext(out TResult current)
    {
        while (true)
        {
            if (_hasInner && _inner.TryGetNext(out current))
            {
                return true;
            }

            if (!_source.TryGetNext(out var outer))
            {
                _hasInner = false;

                // Safe fallback: default value when enumeration has reached the end
                current = default!;
                return false;
            }

            _inner = _selector(outer).Enumerator;
            _hasInner = true;
        }
    }
}
