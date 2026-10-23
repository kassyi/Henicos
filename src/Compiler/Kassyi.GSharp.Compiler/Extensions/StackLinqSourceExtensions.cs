namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>
/// <c>AsStackEnumerable()</c> for the collections the compiler works with, so that StackLinq (the netstandard2.0 copy in
/// <c>Kassyi.ZeroAlloc.Linq.Embedded</c>) runs over them without allocating.
/// </summary>
/// <remarks>
/// Arrays use the copy's own <c>AsStackEnumerable(T[])</c>. A lazy <see cref="IEnumerable{T}"/> such as
/// <c>DescendantNodes()</c> has no adapter: it allocates its enumerator anyway, so a <c>foreach</c> stays clearer.
/// </remarks>
internal static class StackLinqSourceExtensions
{
    public static StackEnumerable<ImmutableArrayEnumerator<T>, T> AsStackEnumerable<T>(this ImmutableArray<T> source) =>
        new(new ImmutableArrayEnumerator<T>(source));

    public static StackEnumerable<ReadOnlyListEnumerator<T>, T> AsStackEnumerable<T>(this IReadOnlyList<T> source) =>
        new(new ReadOnlyListEnumerator<T>(source));

    public static StackEnumerable<SyntaxListEnumerator<TNode>, TNode> AsStackEnumerable<TNode>(this SyntaxList<TNode> source)
        where TNode : SyntaxNode =>
        new(new SyntaxListEnumerator<TNode>(source));

    public static StackEnumerable<SeparatedSyntaxListEnumerator<TNode>, TNode> AsStackEnumerable<TNode>(
        this SeparatedSyntaxList<TNode> source
    )
        where TNode : SyntaxNode =>
        new(new SeparatedSyntaxListEnumerator<TNode>(source));
}

internal struct ImmutableArrayEnumerator<T>(ImmutableArray<T> source) : IStackEnumerator<T>
{
    private int _index;

    public bool TryGetNext(out T current)
    {
        if (!source.IsDefault && _index < source.Length)
        {
            current = source[_index++];
            return true;
        }

        current = default!; // Not read when TryGetNext returns false
        return false;
    }
}

internal struct ReadOnlyListEnumerator<T>(IReadOnlyList<T> source) : IStackEnumerator<T>
{
    private int _index;

    public bool TryGetNext(out T current)
    {
        if (_index < source.Count)
        {
            current = source[_index++];
            return true;
        }

        current = default!; // Not read when TryGetNext returns false
        return false;
    }
}

internal struct SyntaxListEnumerator<TNode>(SyntaxList<TNode> source) : IStackEnumerator<TNode>
    where TNode : SyntaxNode
{
    private int _index;

    public bool TryGetNext(out TNode current)
    {
        if (_index < source.Count)
        {
            current = source[_index++];
            return true;
        }

        current = default!; // Not read when TryGetNext returns false
        return false;
    }
}

internal struct SeparatedSyntaxListEnumerator<TNode>(SeparatedSyntaxList<TNode> source) : IStackEnumerator<TNode>
    where TNode : SyntaxNode
{
    private int _index;

    public bool TryGetNext(out TNode current)
    {
        if (_index < source.Count)
        {
            current = source[_index++];
            return true;
        }

        current = default!; // Not read when TryGetNext returns false
        return false;
    }
}
