namespace Kassyi.GSharp.Compiler.Extensions;

internal struct SyntaxTokenListEnumerator(SyntaxTokenList source) : IStackEnumerator<SyntaxToken>
{
    private int _index;

    public bool TryGetNext(out SyntaxToken current)
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
