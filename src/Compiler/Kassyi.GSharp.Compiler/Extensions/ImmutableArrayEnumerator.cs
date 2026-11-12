namespace Kassyi.GSharp.Compiler.Extensions;

internal struct ImmutableArrayEnumerator<T>(ImmutableArray<T> source) : IStackEnumerator<T>
{
    private int _index;

    public bool TryGetNext([MaybeNullWhen(false)] out T current)
    {
        if (!source.IsDefault && _index < source.Length)
        {
            current = source[_index++];
            return true;
        }

        current = default;
        return false;
    }
}
