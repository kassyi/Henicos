namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Projects each element of a sequence into a new form with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
/// <typeparam name="TResult">The type of the value returned by the selector function.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Evaluates projections lazily with $O(1)$ auxiliary space and zero heap allocations.</para>
/// </remarks>
public ref struct SelectEnumerator<TEnumerator, TSource, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, TResult> _selector;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectEnumerator{TEnumerator, TSource, TResult}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="selector">A transform function to apply to each source element.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    public SelectEnumerator(TEnumerator source, Func<TSource, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _source = source;
        _selector = selector;
    }

    /// <summary>
    /// Attempts to advance the enumerator to the next element and projects it using the selector.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the transformed element;
    /// otherwise, the default value of <typeparamref name="TResult"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator successfully advanced to the next element;
    /// <see langword="false"/> if the end of the sequence was reached.
    /// </returns>
    public bool TryGetNext([MaybeNullWhen(false)] out TResult current)
    {
        if (_source.TryGetNext(out var value))
        {
            current = _selector(value);
            return true;
        }

        // Safe fallback: default value when enumeration has reached the end
        current = default;
        return false;
    }
}
