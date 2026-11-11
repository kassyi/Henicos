namespace Kassyi.GSharp.Compiler.Extensions;

internal struct ReadOnlyListEnumerator<T>(IReadOnlyList<T> source) : IStackEnumerator<T>
{
    private int _index;

    public bool TryGetNext([MaybeNullWhen(false)] out T current)
    {
        if (_index < source.Count)
        {
            current = source[_index++];
            return true;
        }

        current = default;
        return false;
    }
}
