namespace Kassyi.ZeroAlloc.Linq;

public ref struct SkipWhileAtEnumerator<TEnumerator, TSource>(TEnumerator source, Func<TSource, int, bool> predicate)
    : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source = source;
    private bool _yielding = false;
    private int _index = 0;

    public bool TryGetNext(out TSource current)
    {
        while (_source.TryGetNext(out current))
        {
            if (!_yielding && predicate(current, _index++))
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
