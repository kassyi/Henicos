namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// An enumerator that returns a specified number of contiguous elements from the start of a sequence.
/// </summary>
public ref struct TakeEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private int _remaining;

    public TakeEnumerator(TEnumerator source, int count)
    {
        _source = source;
        _remaining = count > 0 ? count : 0;
    }

    public bool TryGetNext(out TSource value)
    {
        if (_remaining > 0 && _source.TryGetNext(out value))
        {
            _remaining--;
            return true;
        }

        value = default!;
        return false;
    }
}
