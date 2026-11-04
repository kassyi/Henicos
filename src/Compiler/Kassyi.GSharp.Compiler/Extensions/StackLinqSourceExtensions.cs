namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>
/// <c>AsStackEnumerable()</c> for the collections the compiler works with, so that StackLinq (the netstandard2.0 copy in
/// <c>Kassyi.ZeroAlloc.Linq.Embedded</c>) runs over them without allocating.
/// </summary>
/// <remarks>
/// Arrays use the copy's own <c>AsStackEnumerable(T[])</c>. Of the lazy sequences, only Roslyn's node walks
/// (<c>DescendantNodes()</c>, <c>ChildNodes()</c>, <c>Ancestors()</c>) have an adapter; see the remarks on it.
/// </remarks>
internal static class StackLinqSourceExtensions
{
    public static StackEnumerable<ImmutableArrayEnumerator<T>, T> AsStackEnumerable<T>(this ImmutableArray<T> source) =>
        new(new ImmutableArrayEnumerator<T>(source));

    public static StackEnumerable<ImmutableArrayEnumerator<T>, T> AsStackEnumerable<T>(this EquatableArray<T> source)
        where T : IEquatable<T> =>
        source.AsImmutableArray().AsStackEnumerable();

    public static StackEnumerable<HashSetEnumerator<T>, T> AsStackEnumerable<T>(this HashSet<T> source) =>
        new(new HashSetEnumerator<T>(source));

    public static StackEnumerable<DictionaryEnumerator<TKey, TValue>, KeyValuePair<TKey, TValue>>
        AsStackEnumerable<TKey, TValue>(this Dictionary<TKey, TValue> source)
        where TKey : notnull =>
        new(new DictionaryEnumerator<TKey, TValue>(source));

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

    public static StackEnumerable<SyntaxTokenListEnumerator, SyntaxToken> AsStackEnumerable(this SyntaxTokenList source) =>
        new(new SyntaxTokenListEnumerator(source));

    /// <summary>Queries a lazy walk over syntax nodes, such as <c>DescendantNodes()</c>.</summary>
    /// <remarks>
    /// The walk allocates its enumerator as a <c>foreach</c> would. The enumerator is disposed when the walk ends, but an
    /// operator that stops early (<c>Any</c>, <c>First</c>) leaves it undisposed; for Roslyn's walks that only means the
    /// pooled traversal stack is not returned to its pool. Use it for node walks only, never for a sequence whose
    /// <c>Dispose</c> releases something.
    /// </remarks>
    public static StackEnumerable<SyntaxNodeWalkEnumerator, SyntaxNode> AsStackEnumerable(this IEnumerable<SyntaxNode> nodes) =>
        new(new SyntaxNodeWalkEnumerator(nodes));
}

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

internal struct SyntaxListEnumerator<TNode>(SyntaxList<TNode> source) : IStackEnumerator<TNode>
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
