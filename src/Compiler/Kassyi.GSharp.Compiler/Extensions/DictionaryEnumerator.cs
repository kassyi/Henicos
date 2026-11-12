namespace Kassyi.GSharp.Compiler.Extensions;

internal struct DictionaryEnumerator<TKey, TValue>(Dictionary<TKey, TValue> source)
    : IStackEnumerator<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private Dictionary<TKey, TValue>.Enumerator _enumerator = source.GetEnumerator();

    public bool TryGetNext(out KeyValuePair<TKey, TValue> current)
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
