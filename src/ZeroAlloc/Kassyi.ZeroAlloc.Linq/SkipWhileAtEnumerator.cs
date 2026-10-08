namespace Kassyi.ZeroAlloc.Linq;

public ref struct SkipWhileAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;
    private readonly Func<TSource, int, bool> _predicate;
    private bool _yielding;
    private int _index;

    public SkipWhileAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
        _yielding = false;
        _index = 0;
    }

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
