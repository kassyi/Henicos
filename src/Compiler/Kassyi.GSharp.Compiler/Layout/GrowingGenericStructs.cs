namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// A generic struct whose members name a larger construction of itself: by value (<c>G&lt;T&gt; { G&lt;G&lt;T&gt;&gt;
/// Inner; }</c> and <c>A&lt;T&gt; { B&lt;A&lt;A&lt;T&gt;&gt;&gt; Inner; }</c>, C# error CS0523) or through arrays
/// (<c>N&lt;T&gt; { N&lt;T[]&gt;[] Kids; }</c>, valid C#). A walk over its members meets <c>G&lt;int&gt;</c>,
/// <c>G&lt;G&lt;int&gt;&gt;</c>, ... and never the same constructed type twice, so a set of the structs already met
/// does not stop it on its own.
/// </summary>
internal static class GrowingGenericStructs
{
    /// <summary>
    /// For a walk with a set of the structs it has met that follows arrays, properties or type arguments: whether
    /// <paramref name="type"/> is a construction that grows out of one in <paramref name="met"/> (each type argument
    /// holds the earlier one's, as <c>G&lt;int[,]&gt;</c> holds <c>int</c>) of a definition whose members name a
    /// construction of itself. Another construction (<c>G&lt;M&gt;</c> after <c>G&lt;int&gt;</c>) is searched once
    /// like any struct, and so is <c>Pair&lt;Pair&lt;int&gt;&gt;</c>, whose definition names only its type argument.
    /// </summary>
    /// <remarks>
    /// Known limit: what only the larger construction holds (<c>G&lt;T&gt; { G&lt;T[,]&gt;[] K; T Leaf; }</c>, where
    /// the <c>Leaf</c> of <c>G&lt;int[,]&gt;</c> is a multidimensional array) is not searched.
    /// </remarks>
    public static bool IsLargerConstruction<T>(ITypeSymbol type, HashSet<T> met)
        where T : class, ITypeSymbol =>
        type is INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Struct } named &&
        met
            .AsStackEnumerable()
            .Any(named, static (other, entered) => other is INamedTypeSymbol earlier && GrowsOutOf(entered, earlier)) &&
        NamesItself(named.OriginalDefinition);

    /// <summary>
    /// For a layout walk, which follows fields by value: whether <paramref name="structType"/> is a construction of a
    /// definition that holds a construction of itself by value (the CS0523 rule), and <paramref name="path"/> holds
    /// another construction of it. <c>V&lt;V&lt;int&gt;&gt;</c> of
    /// <c>V&lt;T&gt; { T X; V&lt;T&gt; Swapped =&gt; default; }</c> is laid out in full.
    /// </summary>
    public static bool HoldsAnotherConstructionByValue(INamedTypeSymbol structType, HashSet<INamedTypeSymbol> path) =>
        structType.IsGenericType &&
        path
            .AsStackEnumerable()
            .Any(structType, static (other, entered) =>
                !SymbolEqualityComparer.Default.Equals(other, entered) &&
                SymbolEqualityComparer.Default.Equals(other.OriginalDefinition, entered.OriginalDefinition)) &&
        HoldsItselfByValue(structType.OriginalDefinition);

    // A construction of the same definition whose every type argument holds the earlier one's
    private static bool GrowsOutOf(INamedTypeSymbol later, INamedTypeSymbol earlier)
    {
        if (SymbolEqualityComparer.Default.Equals(later, earlier) ||
            !SymbolEqualityComparer.Default.Equals(later.OriginalDefinition, earlier.OriginalDefinition))
        {
            return false;
        }

        for (var i = 0; i < later.TypeArguments.Length; i++)
        {
            if (!Holds(later.TypeArguments[i], earlier.TypeArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Whether part is whole, or an element or type argument of it at any depth
    private static bool Holds(ITypeSymbol whole, ITypeSymbol part) =>
        SymbolEqualityComparer.Default.Equals(whole, part) ||
        whole switch
        {
            IArrayTypeSymbol array => Holds(array.ElementType, part),
            IPointerTypeSymbol pointer => Holds(pointer.PointedAtType, part),
            INamedTypeSymbol named => named.TypeArguments
                .AsStackEnumerable()
                .Any(part, static (argument, p) => Holds(argument, p)),
            _ => false
        };

    /// <summary>
    /// The CS0523 rule on constructed field types: whether a field of <paramref name="definition"/>, or of a struct
    /// held by value below it (as constructed there: <c>B&lt;A&lt;A&lt;T&gt;&gt;&gt;.V</c> is
    /// <c>A&lt;A&lt;T&gt;&gt;</c>, a Nullable's value included), is a construction of <paramref name="definition"/>.
    /// </summary>
    public static bool HoldsItselfByValue(INamedTypeSymbol definition)
    {
        var searched = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { definition };
        var pending = new Queue<INamedTypeSymbol>();
        pending.Enqueue(definition);
        while (pending.Count > 0)
        {
            foreach (var member in pending.Dequeue().GetMembers())
            {
                if (member is not IFieldSymbol { IsStatic: false } field ||
                    field.Type.UnwrapNullable() is not INamedTypeSymbol { TypeKind: TypeKind.Struct } held)
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(held.OriginalDefinition, definition))
                {
                    return true;
                }

                QueueOnce(held, searched, pending);
            }
        }

        return false;
    }

    // Queues a construction not searched yet. [WHY] One that grows out of a searched construction of its definition
    // (B<B<int>> after B<int> of B<U> { B<B<U>> Inner; }) is not queued: it would make the search endless, and what it
    // holds by value is what the smaller one holds, with larger type arguments
    private static void QueueOnce(INamedTypeSymbol held, HashSet<INamedTypeSymbol> searched,
        Queue<INamedTypeSymbol> pending)
    {
        if (!searched.Contains(held) &&
            !searched
                .AsStackEnumerable()
                .Any(held, static (earlier, later) => GrowsOutOf(later, earlier)) &&
            searched.Add(held))
        {
            pending.Enqueue(held);
        }
    }

    // Whether a member type of definition, or of the definition of a struct that a member type names, names a
    // construction of definition; through arrays, Nullable, pointers and type arguments, in fields and properties.
    // [WHY] A construction that substitution brings in (B<A<A<T>>>.V is A<A<T>>) was written as a type argument, which
    // is collected, so the definitions are enough; they are finitely many, so the search ends
    private static bool NamesItself(INamedTypeSymbol definition)
    {
        var searched = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { definition };
        var pending = new Queue<INamedTypeSymbol>();
        pending.Enqueue(definition);
        var named = new List<INamedTypeSymbol>();
        while (pending.Count > 0)
        {
            named.Clear();
            foreach (var member in pending.Dequeue().GetMembers())
            {
                CollectMemberStructs(member, named);
            }

            if (named
                .AsStackEnumerable()
                .Any((definition, searched, pending), static (held, state) =>
                    MatchesOrQueues(held, state.definition, state.searched, state.pending)))
            {
                return true;
            }
        }

        return false;
    }

    // True when held is a construction of definition; otherwise queues its user definition once
    private static bool MatchesOrQueues(INamedTypeSymbol held, INamedTypeSymbol definition,
        HashSet<INamedTypeSymbol> searched, Queue<INamedTypeSymbol> pending)
    {
        if (SymbolEqualityComparer.Default.Equals(held.OriginalDefinition, definition))
        {
            return true;
        }

        // [WHY] A BCL struct names no user definition through its members; its type arguments are already collected
        if (!held.ContainingNamespace.IsSystemOrNumerics() && searched.Add(held.OriginalDefinition))
        {
            pending.Enqueue(held.OriginalDefinition);
        }

        return false;
    }

    // The structs that the type of an instance field or property names
    private static void CollectMemberStructs(ISymbol member, List<INamedTypeSymbol> structs)
    {
        switch (member)
        {
            case IFieldSymbol { IsStatic: false } field:
                CollectStructs(field.Type, structs);
                break;
            case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                CollectStructs(property.Type, structs);
                break;
        }
    }

    // The structs that type names: itself, the elements of arrays and pointers, and its type arguments
    private static void CollectStructs(ITypeSymbol type, List<INamedTypeSymbol> structs)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                CollectStructs(array.ElementType, structs);
                break;
            case IPointerTypeSymbol pointer:
                CollectStructs(pointer.PointedAtType, structs);
                break;
            case INamedTypeSymbol named:
                if (named.TypeKind == TypeKind.Struct)
                {
                    structs.Add(named);
                }

                foreach (var typeArgument in named.TypeArguments)
                {
                    CollectStructs(typeArgument, structs);
                }

                break;
        }
    }
}
