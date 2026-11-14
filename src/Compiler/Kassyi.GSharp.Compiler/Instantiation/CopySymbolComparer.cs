namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Compares a symbol of a generic declaration, read as the instance (<see cref="InstanceSymbolMap"/>), with the symbol
/// the closed copy binds in the derived compilation. Symbols of two compilations are never equal as objects, so types
/// and members are compared by their documentation IDs and type arguments, and what is declared inside the declaration
/// (locals, parameters, lambdas, local functions) by the node that declares it.
/// </summary>
/// <param name="map">The type arguments of the instance.</param>
/// <param name="originalNodes">
/// The node number of every node inside the declaration, the declaration itself left out (a recursive call binds the
/// declaration, which the copy calls by its constructed name too).
/// </param>
internal sealed class CopySymbolComparer(InstanceSymbolMap map, IReadOnlyDictionary<SyntaxNode, int> originalNodes)
{
    /// <summary>
    /// Whether the copy binds <paramref name="copy"/> where the declaration binds <paramref name="original"/>.
    /// </summary>
    /// <param name="original">The symbol of the declaration, already read as the instance.</param>
    /// <param name="copy">The symbol the copy binds.</param>
    public bool Same(ISymbol? original, ISymbol? copy)
    {
        if (original is null || copy is null)
        {
            return original is null && copy is null;
        }

        if (IsDeclaredInOriginal(original) || IsDeclaredInCopy(copy))
        {
            return SameDeclaration(original, copy);
        }

        return (original, copy) switch
        {
            (IParameterSymbol left, IParameterSymbol right) =>
                left.Ordinal == right.Ordinal && Same(map.Substitute(left.ContainingSymbol), right.ContainingSymbol),
            (ITypeSymbol left, ITypeSymbol right) => SameType(left, right),
            (IMethodSymbol left, IMethodSymbol right) => SameMethod(left, right.GetConstructedReducedFrom() ?? right),
            (IPropertySymbol or IFieldSymbol or IEventSymbol, IPropertySymbol or IFieldSymbol or IEventSymbol) =>
                SameDefinition(original, copy) && SameType(original.ContainingType, copy.ContainingType),
            (IDiscardSymbol left, IDiscardSymbol right) => SameType(left.Type, right.Type),
            _ => original.Kind == copy.Kind && SameDefinition(original, copy),
        };
    }

    /// <summary>Whether two types are the same type, compared across the two compilations.</summary>
    public bool SameType(ITypeSymbol? original, ITypeSymbol? copy)
    {
        if (original is null || copy is null)
        {
            return original is null && copy is null;
        }

        switch (original, copy)
        {
            case ({ TypeKind: TypeKind.Error }, _) or (_, { TypeKind: TypeKind.Error }):
                return false;
            case (IArrayTypeSymbol left, IArrayTypeSymbol right):
                return left.Rank == right.Rank && SameType(left.ElementType, right.ElementType);
            case (IPointerTypeSymbol left, IPointerTypeSymbol right):
                return SameType(left.PointedAtType, right.PointedAtType);
            case (ITypeParameterSymbol left, ITypeParameterSymbol right):
                // A type parameter of a local function or lambda inside the declaration
                return left.Ordinal == right.Ordinal && left.TypeParameterKind == right.TypeParameterKind &&
                       left.Name == right.Name;
            case (INamedTypeSymbol left, INamedTypeSymbol right):
                return SameDefinition(left.OriginalDefinition, right.OriginalDefinition) &&
                       SameType(left.ContainingType, right.ContainingType) &&
                       SameTypes(left.TypeArguments, right.TypeArguments);
            default:
                return false;
        }
    }

    private bool SameMethod(IMethodSymbol original, IMethodSymbol copy)
    {
        if (original.MethodKind == MethodKind.BuiltinOperator || copy.MethodKind == MethodKind.BuiltinOperator)
        {
            return original.MethodKind == copy.MethodKind && original.Name == copy.Name &&
                   SameType(original.ContainingType, copy.ContainingType) && SameParameterTypes(original, copy);
        }

        return SameDefinition(original.OriginalDefinition, copy.OriginalDefinition) &&
               SameType(original.ContainingType, copy.ContainingType) &&
               SameTypes(original.TypeArguments, copy.TypeArguments);
    }

    private bool SameParameterTypes(IMethodSymbol original, IMethodSymbol copy)
    {
        if (original.Parameters.Length != copy.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < original.Parameters.Length; i++)
        {
            if (!SameType(original.Parameters[i].Type, copy.Parameters[i].Type))
            {
                return false;
            }
        }

        return true;
    }

    private bool SameTypes(ImmutableArray<ITypeSymbol> original, ImmutableArray<ITypeSymbol> copy)
    {
        if (original.Length != copy.Length)
        {
            return false;
        }

        for (var i = 0; i < original.Length; i++)
        {
            if (!SameType(original[i], copy[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Declared by a node of the declaration: matched through the node numbers, then by kind and type
    private bool SameDeclaration(ISymbol original, ISymbol copy)
    {
        if (original.Kind != copy.Kind ||
            original.DeclaringSyntaxReferences is not [var originalReference, ..] ||
            copy.DeclaringSyntaxReferences is not [var copyReference, ..] ||
            !originalNodes.TryGetValue(originalReference.GetSyntax(), out var originalIndex) ||
            !CopyNodeIndex.TryGet(copyReference.GetSyntax(), out var copyIndex) || originalIndex != copyIndex)
        {
            return false;
        }

        return (original, copy) switch
        {
            (IMethodSymbol left, IMethodSymbol right) => SameSubstitutedTypes(left.TypeArguments, right.TypeArguments),
            _ => SameType(GetDeclaredType(original), GetDeclaredType(copy)),
        };
    }

    private bool SameSubstitutedTypes(ImmutableArray<ITypeSymbol> original, ImmutableArray<ITypeSymbol> copy)
    {
        if (original.Length != copy.Length)
        {
            return false;
        }

        for (var i = 0; i < original.Length; i++)
        {
            if (!SameType(map.Substitute(original[i]), copy[i]))
            {
                return false;
            }
        }

        return true;
    }

    private ITypeSymbol? GetDeclaredType(ISymbol symbol) =>
        symbol switch
        {
            ILocalSymbol local => map.Substitute(local.Type),
            IParameterSymbol parameter => map.Substitute(parameter.Type),
            IRangeVariableSymbol => null,
            _ => null,
        };

    private bool IsDeclaredInOriginal(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences is [var reference, ..] && originalNodes.ContainsKey(reference.GetSyntax());

    // The copy itself (node 0) is the declaration, not something declared inside it
    private static bool IsDeclaredInCopy(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences is [var reference, ..] &&
        CopyNodeIndex.TryGet(reference.GetSyntax(), out var index) && index > 0;

    private static bool SameDefinition(ISymbol original, ISymbol copy)
    {
        var left = DocumentationCommentId.CreateDeclarationId(original);
        var right = DocumentationCommentId.CreateDeclarationId(copy);
        return left is not null && string.Equals(left, right, StringComparison.Ordinal);
    }
}
