namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// An enumerator that bypasses a specified number of elements in a sequence and then returns the remaining elements with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Operates lazily with $O(1)$ auxiliary space and zero heap allocations.</para>
/// </remarks>
public ref struct SkipEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private int _remaining;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipEnumerator{TEnumerator, TSource}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="count">The number of elements to skip before yielding elements.</param>
    public SkipEnumerator(TEnumerator source, int count)
    {
        _source = source;
        _remaining = count > 0 ? count : 0;
    }

    /// <summary>
    /// Advances the enumerator to the next element of the sequence after skipping.
    /// </summary>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, contains the next element in the sequence;
    /// otherwise, the default value of <typeparamref name="TSource"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator was successfully advanced to the next element;
    /// <see langword="false"/> if the end of the sequence was reached.
    /// </returns>
    public bool TryGetNext(out TSource value)
    {
        while (_remaining > 0)
        {
            if (_source.TryGetNext(out _))
            {
                _remaining--;
            }
            else
            {
                value = default!;
                return false;
            }
        }

        return _source.TryGetNext(out value);
    }
}
