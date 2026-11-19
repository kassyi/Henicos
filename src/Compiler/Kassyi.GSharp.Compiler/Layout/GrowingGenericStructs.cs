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
    /// Whether <paramref name="type"/> is a construction of a generic struct whose members name a construction of its
    /// own definition (<see cref="GrowingGenericStructs"/>), and <paramref name="met"/> already holds another
    /// construction of that definition: the walk is where the construction grows, and stops.
    /// </summary>
    /// <remarks>
    /// The definition is only searched when the set holds another construction of it, so a walk without such a pair
    /// pays one look at the set per generic struct. <c>Pair&lt;Pair&lt;int&gt;&gt;</c> is not stopped: the definition
    /// <c>Pair&lt;T&gt;</c> names only its type argument.
    /// </remarks>
    public static bool IsLargerConstruction<T>(ITypeSymbol type, HashSet<T> met)
        where T : class, ITypeSymbol =>
        type is INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Struct } named &&
        met
            .AsStackEnumerable()
            .Any(named, static (other, entered) =>
                !SymbolEqualityComparer.Default.Equals(other, entered) &&
                SymbolEqualityComparer.Default.Equals(other.OriginalDefinition, entered.OriginalDefinition)) &&
        NamesItself(named.OriginalDefinition);

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
            foreach (var member in pending.Dequeue().GetMembers())
            {
                named.Clear();
                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    CollectStructs(field.Type, named);
                }
                else if (member is IPropertySymbol { IsStatic: false, IsIndexer: false } property)
                {
                    CollectStructs(property.Type, named);
                }

                foreach (var held in named)
                {
                    if (SymbolEqualityComparer.Default.Equals(held.OriginalDefinition, definition))
                    {
                        return true;
                    }

                    // [WHY] A BCL struct names no user definition through its members; its type arguments are
                    // already collected
                    if (!held.ContainingNamespace.IsSystemOrNumerics() && searched.Add(held.OriginalDefinition))
                    {
                        pending.Enqueue(held.OriginalDefinition);
                    }
                }
            }
        }

        return false;
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
