namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// An enumerator that bypasses elements in a sequence as long as a specified condition involving element index is true and then returns the remaining elements with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Operates lazily with $O(1)$ auxiliary space and zero heap allocations.</para>
/// </remarks>
public ref struct SkipWhileAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, int, bool> _predicate;
    private bool _yielding;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipWhileAtEnumerator{TEnumerator, TSource}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    /// <param name="predicate">A function to test each element and its index for a condition.</param>
    public SkipWhileAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
        _yielding = false;
        _index = 0;
    }

    /// <summary>
    /// Advances the enumerator to the next element of the sequence after elements matching the predicate have been skipped.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the next element in the sequence;
    /// otherwise, the default value of <typeparamref name="TSource"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the enumerator was successfully advanced to the next element;
    /// <see langword="false"/> if the end of the sequence was reached.
    /// </returns>
    public bool TryGetNext(out TSource current)
    {
        while (_source.TryGetNext(out current))
        {
            if (!_yielding && _predicate(current, _index++))
            {
                continue;
            }

            _yielding = true;
            return true;
        }

        current = default!;
        return false;
    }
}
