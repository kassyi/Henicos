namespace Kassyi.GSharp.Compiler.Extensions;

internal struct SeparatedSyntaxListEnumerator<TNode>(SeparatedSyntaxList<TNode> source) : IStackEnumerator<TNode>
    where TNode : SyntaxNode
{
    private int _index;

    public bool TryGetNext([MaybeNullWhen(false)] out TNode current)
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
