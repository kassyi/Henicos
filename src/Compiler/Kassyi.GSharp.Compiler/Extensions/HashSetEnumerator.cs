namespace Kassyi.GSharp.Compiler.Extensions;

// HashSet<T>.Enumerator and Dictionary<TKey, TValue>.Enumerator are structs whose Dispose does nothing, so stopping early
// loses nothing
internal struct HashSetEnumerator<T>(HashSet<T> source) : IStackEnumerator<T>
{
    private HashSet<T>.Enumerator _enumerator = source.GetEnumerator();

    public bool TryGetNext([MaybeNullWhen(false)] out T current)
    {
        if (_enumerator.MoveNext())
        {
            current = _enumerator.Current;
            return true;
        }

        current = default;
        return false;
    }
}
