namespace Kassyi.ZeroAlloc.Linq;

public ref struct SkipWhileEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, bool> _predicate;
    private bool _yielding;

    public SkipWhileEnumerator(TEnumerator source, Func<TSource, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
        _yielding = false;
    }

    public bool TryGetNext(out TSource current)
    {
        while (_source.TryGetNext(out current))
        {
            if (!_yielding && _predicate(current))
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
