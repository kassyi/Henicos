namespace Kassyi.ZeroAlloc.Linq;

public ref struct TakeWhileEnumerator<TEnumerator, TSource>(TEnumerator source, Func<TSource, bool> predicate)
    : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source = source;

    public bool TryGetNext(out TSource current)
    {
        if (_source.TryGetNext(out current))
        {
            if (predicate(current))
            {
                return true;
            }
        }

        current = default!;
        return false;
    }
}
