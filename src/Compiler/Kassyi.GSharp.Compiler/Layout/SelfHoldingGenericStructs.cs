namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// A generic struct whose definition holds a larger construction of itself by value
/// (<c>G&lt;T&gt; { G&lt;G&lt;T&gt;&gt; Inner; }</c>, C# error CS0523): a walk over its fields meets
/// <c>G&lt;int&gt;</c>, <c>G&lt;G&lt;int&gt;&gt;</c>, ... and never the same constructed type twice, so a set of the
/// structs already met does not stop it on its own.
/// </summary>
internal static class SelfHoldingGenericStructs
{
    /// <summary>
    /// Whether <paramref name="type"/> is a construction of a generic struct whose definition holds itself by value,
    /// and <paramref name="met"/> already holds another construction of that definition: the walk is at the cycle.
    /// </summary>
    /// <remarks>
    /// The definition is only searched when the set holds another construction of it, so a walk of structs without
    /// such a pair pays one look at the set per generic struct. <c>Pair&lt;Pair&lt;int&gt;&gt;</c> is no cycle: the
    /// definition <c>Pair&lt;T&gt;</c> holds only its type argument.
    /// </remarks>
    public static bool IsLargerConstruction<T>(ITypeSymbol type, HashSet<T> met)
        where T : class, ITypeSymbol =>
        type is INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Struct } named &&
        met
            .AsStackEnumerable()
            .Any(named, static (other, entered) =>
                !SymbolEqualityComparer.Default.Equals(other, entered) &&
                SymbolEqualityComparer.Default.Equals(other.OriginalDefinition, entered.OriginalDefinition)) &&
        HoldsItselfByValue(named.OriginalDefinition);

    // The CS0523 test on definitions: a field of definition, or of a struct a field of it holds by value (a Nullable
    // included), whose definition is definition
    private static bool HoldsItselfByValue(INamedTypeSymbol definition)
    {
        var searched = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { definition };
        var pending = new Stack<INamedTypeSymbol>();
        pending.Push(definition);
        while (pending.Count > 0)
        {
            foreach (var member in pending.Pop().GetMembers())
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

                if (searched.Add(held.OriginalDefinition))
                {
                    pending.Push(held.OriginalDefinition);
                }
            }
        }

        return false;
    }
}
