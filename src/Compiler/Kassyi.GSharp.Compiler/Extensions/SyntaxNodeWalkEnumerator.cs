namespace Kassyi.GSharp.Compiler.Extensions;

internal struct SyntaxNodeWalkEnumerator(IEnumerable<SyntaxNode> nodes) : IStackEnumerator<SyntaxNode>
{
    private IEnumerator<SyntaxNode>? _enumerator;
    private bool _finished;

    public bool TryGetNext([MaybeNullWhen(false)] out SyntaxNode current)
    {
        if (!_finished)
        {
            _enumerator ??= nodes.GetEnumerator();
            if (_enumerator.MoveNext())
            {
                current = _enumerator.Current;
                return true;
            }

            _enumerator.Dispose();
            _finished = true;
        }

        current = default;
        return false;
    }
}
