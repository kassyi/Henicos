namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Filters a sequence of elements based on a predicate with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Evaluates elements lazily with $O(1)$ auxiliary space and zero heap allocations.</para>
/// </remarks>
public ref struct WhereEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, bool> _predicate;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhereEnumerator{TEnumerator, TSource}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    public WhereEnumerator(TEnumerator source, Func<TSource, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _source = source;
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
            if (_predicate(value))
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
