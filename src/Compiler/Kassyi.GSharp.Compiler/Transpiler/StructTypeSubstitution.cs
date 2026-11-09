namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Replaces the type parameters of a constructed generic struct with its type arguments.</summary>
internal static class StructTypeSubstitution
{
    /// <summary>
    /// Substitutes type parameters of <paramref name="currentStruct"/> in <paramref name="typeSymbol"/> with the
    /// concrete type arguments of <paramref name="currentStruct"/>.
    /// </summary>
    internal static ITypeSymbol? SubstituteTypeParameters(ITypeSymbol? typeSymbol, INamedTypeSymbol? currentStruct,
        Compilation? compilation = null)
    {
        if (typeSymbol == null || currentStruct is not { IsGenericType: true } || currentStruct.IsDefinition)
        {
            return typeSymbol;
        }

        return SubstituteTypeParametersCore(typeSymbol, currentStruct, compilation);
    }

    private static ITypeSymbol SubstituteTypeParametersCore(ITypeSymbol typeSymbol, INamedTypeSymbol currentStruct,
        Compilation? compilation)
    {
        if (typeSymbol is ITypeParameterSymbol typeParam)
        {
            return FindTypeArgument(typeParam, currentStruct) ?? typeSymbol;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType && compilation != null)
        {
            var elem = SubstituteTypeParametersCore(arrayType.ElementType, currentStruct, compilation);
            return ReferenceEquals(elem, arrayType.ElementType)
                ? arrayType
                : compilation.CreateArrayTypeSymbol(elem, arrayType.Rank);
        }

        if (typeSymbol is IPointerTypeSymbol pointerType && compilation != null)
        {
            var pointedAt = SubstituteTypeParametersCore(pointerType.PointedAtType, currentStruct, compilation);
            return ReferenceEquals(pointedAt, pointerType.PointedAtType)
                ? pointerType
                : compilation.CreatePointerTypeSymbol(pointedAt);
        }

        // [WHY] A definition counts as well: inside its own members, a generic struct names itself as the open
        // Box<T>, whose type arguments are its type parameters
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return SubstituteTypeArguments(namedType, currentStruct, compilation);
        }

        return typeSymbol;
    }

    // The type argument of currentStruct that stands for typeParam, or null when typeParam is not one of its own
    private static ITypeSymbol? FindTypeArgument(ITypeParameterSymbol typeParam, INamedTypeSymbol currentStruct)
    {
        var typeParams = currentStruct.TypeParameters;
        for (var i = 0; i < typeParams.Length; i++)
        {
            if (typeParams[i].IsEqualTo(typeParam))
            {
                return currentStruct.TypeArguments[i];
            }
        }

        return null;
    }

    // namedType constructed again from its substituted type arguments, or namedType itself when none of them changes
    private static INamedTypeSymbol SubstituteTypeArguments(INamedTypeSymbol namedType,
        INamedTypeSymbol currentStruct, Compilation? compilation)
    {
        var args = namedType.TypeArguments;
        ITypeSymbol[]? substituted = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = SubstituteTypeParametersCore(args[i], currentStruct, compilation);
            if (substituted == null)
            {
                if (ReferenceEquals(arg, args[i]))
                {
                    continue;
                }

                substituted = CopyPrefix(args, i);
            }

            substituted[i] = arg;
        }

        return substituted != null ? namedType.OriginalDefinition.Construct(substituted) : namedType;
    }

    // A new array of args.Length elements holding the first count elements of args
    private static ITypeSymbol[] CopyPrefix(ImmutableArray<ITypeSymbol> args, int count)
    {
        var copy = new ITypeSymbol[args.Length];
        for (var j = 0; j < count; j++)
        {
            copy[j] = args[j];
        }

        return copy;
    }
}
