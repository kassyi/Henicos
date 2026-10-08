namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Filters a sequence of elements based on a predicate that incorporates the element's index.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements to enumerate.</typeparam>
public ref struct WhereAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, int, bool> _predicate;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhereAtEnumerator{TEnumerator, TSource}"/> struct.
    /// </summary>
    /// <param name="source">The source enumerator.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter of the function represents the index of the source element.</param>
    public WhereAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
        _index = 0;
    }

    /// <inheritdoc/>
    public bool TryGetNext(out TSource current)
    {
        while (_source.TryGetNext(out var value))
        {
            if (!_predicate(value, _index++))
            {
                continue;
            }

            current = value;
            return true;
        }

        current = default!;
        return false;
    }
}
