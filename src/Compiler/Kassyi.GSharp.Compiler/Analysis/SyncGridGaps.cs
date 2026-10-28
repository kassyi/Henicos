namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>The gaps of a Gpu.SyncGrid body, between its flattened statements (SyncGridBody) (<see cref="CrossBarrierLiveness"/>).</summary>
internal sealed class SyncGridGaps(ImmutableArray<SyncGridElement> elements, SemanticModel model)
{
    // The statement that contains syntax, or the statements that syntax contains (a nested Gpu.SyncGrid(body))
    public SyncGridElementRange? RangeOf(SyntaxNode syntax)
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < elements.Length; i++)
        {
            var span = elements[i].Node.Span;
            if (span.Contains(syntax.Span))
            {
                return new SyncGridElementRange(i, i);
            }

            if (syntax.Span.Contains(span))
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        return first < 0 ? null : new SyncGridElementRange(first, last);
    }

    // Whether a grid sync follows one of the statements from 'from' up to the one before 'to'
    public bool HasGap(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (SyncGridBody.IsSyncPoint(elements, i, model))
            {
                return true;
            }
        }

        return false;
    }
}
