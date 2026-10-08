namespace Kassyi.ZeroAlloc.Linq;

public ref struct TakeWhileAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, int, bool> _predicate;
    private int _index;

    public TakeWhileAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
        _index = 0;
    }

    public bool TryGetNext(out TSource current)
    {
        if (_source.TryGetNext(out current))
        {
            if (_predicate(current, _index++))
            {
                return true;
            }
        }
        current = default!;
        return false;
    }
}
