namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// An enumerator that bypasses a specified number of elements in a sequence and then returns the remaining elements.
/// </summary>
public ref struct SkipEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private int _remaining;

    public SkipEnumerator(TEnumerator source, int count)
    {
        _source = source;
        _remaining = count > 0 ? count : 0;
    }

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
