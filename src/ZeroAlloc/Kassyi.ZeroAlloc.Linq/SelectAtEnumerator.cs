namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Projects each element of a sequence into a new form by incorporating the element's index.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements to enumerate.</typeparam>
/// <typeparam name="TResult">The type of the projected elements.</typeparam>
public ref struct SelectAtEnumerator<TEnumerator, TSource, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, int, TResult> _selector;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectAtEnumerator{TEnumerator, TSource, TResult}"/> struct.
    /// </summary>
    /// <param name="source">The source enumerator.</param>
    /// <param name="selector">A transform function to apply to each source element; the second parameter of the function represents the index of the source element.</param>
    public SelectAtEnumerator(TEnumerator source, Func<TSource, int, TResult> selector)
    {
        _source = source;
        _selector = selector;
        _index = 0;
    }

    /// <inheritdoc/>
    public bool TryGetNext(out TResult current)
    {
        if (_source.TryGetNext(out var value))
        {
            current = _selector(value, _index++);
            return true;
        }

        current = default!;
        return false;
    }
}
