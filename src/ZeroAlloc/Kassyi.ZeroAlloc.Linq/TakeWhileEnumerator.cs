namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// An enumerator that returns elements from a sequence as long as a specified condition is true with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Operates lazily with $O(1)$ auxiliary space and zero heap allocations.</para>
/// </remarks>
public ref struct TakeWhileEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, bool> _predicate;

    /// <summary>
    /// Initializes a new instance of the <see cref="TakeWhileEnumerator{TEnumerator, TSource}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    public TakeWhileEnumerator(TEnumerator source, Func<TSource, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
    }

    /// <summary>
    /// Advances the enumerator to the next element of the sequence as long as the element satisfies the condition.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the next element in the sequence;
    /// otherwise, the default value of <typeparamref name="TSource"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator was successfully advanced to the next element satisfying the predicate;
    /// <see langword="false"/> if the end of the sequence was reached or the predicate returned false.
    /// </returns>
    public bool TryGetNext([MaybeNullWhen(false)] out TSource current)
    {
        if (_source.TryGetNext(out current))
        {
            if (_predicate(current))
            {
                return true;
            }
        }

        current = default;
        return false;
    }
}
