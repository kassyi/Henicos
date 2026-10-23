namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Filters a sequence of elements based on a predicate that receives a caller-supplied state, with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
/// <typeparam name="TState">The type of the state passed to the predicate.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Evaluates elements lazily with $O(1)$ auxiliary space. Passing the outer value as state lets
/// the predicate be a <see langword="static"/> lambda, so no closure is allocated.</para>
/// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
/// </remarks>
public ref struct WhereWithStateEnumerator<TEnumerator, TSource, TState> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly TState _state;
    private readonly Func<TSource, TState, bool> _predicate;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhereWithStateEnumerator{TEnumerator, TSource, TState}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    public WhereWithStateEnumerator(TEnumerator source, TState state, Func<TSource, TState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _source = source;
        _state = state;
        _predicate = predicate;
    }

    /// <summary>
    /// Attempts to advance the enumerator to the next element that satisfies the predicate.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the next element satisfying the predicate;
    /// otherwise, the default value of <typeparamref name="TSource"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a matching element is found;
    /// <see langword="false"/> if the end of the sequence was reached without finding another match.
    /// </returns>
    public bool TryGetNext(out TSource current)
    {
        while (_source.TryGetNext(out var value))
        {
            if (_predicate(value, _state))
            {
                current = value;
                return true;
            }
        }

        // Safe fallback: default value when enumeration has reached the end
        current = default!;
        return false;
    }
}
