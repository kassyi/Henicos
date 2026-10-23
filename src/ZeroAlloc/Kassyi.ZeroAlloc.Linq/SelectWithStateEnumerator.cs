namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Projects each element of a sequence into a new form with a selector that receives a caller-supplied state, with zero
/// heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
/// <typeparam name="TState">The type of the state passed to the selector.</typeparam>
/// <typeparam name="TResult">The type of the value returned by the selector function.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Evaluates projections lazily with $O(1)$ auxiliary space. Passing the outer value as state
/// lets the selector be a <see langword="static"/> lambda, so no closure is allocated.</para>
/// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
/// </remarks>
public ref struct SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly TState _state;
    private readonly Func<TSource, TState, TResult> _selector;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectWithStateEnumerator{TEnumerator, TSource, TState, TResult}"/>
    /// struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="state">The value passed to every call of <paramref name="selector"/>.</param>
    /// <param name="selector">A transform function to apply to each source element; the second parameter is <paramref name="state"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    public SelectWithStateEnumerator(TEnumerator source, TState state, Func<TSource, TState, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _source = source;
        _state = state;
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
    public bool TryGetNext(out TResult current)
    {
        if (_source.TryGetNext(out var value))
        {
            current = _selector(value, _state);
            return true;
        }

        // Safe fallback: default value when enumeration has reached the end
        current = default!;
        return false;
    }
}
