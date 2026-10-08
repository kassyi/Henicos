namespace Kassyi.ZeroAlloc.Linq;

public ref struct TakeWhileEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, bool> _predicate;

    public TakeWhileEnumerator(TEnumerator source, Func<TSource, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
    }

    public bool TryGetNext(out TSource current)
    {
        if (_source.TryGetNext(out current))
        {
            if (_predicate(current))
            {
                return true;
            }
        }
        current = default!;
        return false;
    }
}
