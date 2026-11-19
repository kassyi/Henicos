namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>
/// Propagates array-like field aliases across whole-struct copies for <see cref="AliasSources"/>.
/// </summary>
internal static class AliasStructCopySources
{
    public static bool TryAdd(AliasSources sources, AliasKey destination, ExpressionSyntax value)
    {
        if (AliasKey.Of(value, sources.Model) is not { } sourceKey ||
            TypeOf(destination.Last) is not
                INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } targetType ||
            targetType.TryGetArrayLikeElementType(out _) ||
            !SymbolEqualityComparer.Default.Equals(targetType, TypeOf(sourceKey.Last)))
        {
            return false;
        }

        if (!destination.Equals(sourceKey))
        {
            AddStructCopy(sources, destination, sourceKey, targetType, path: null);
        }

        return true;
    }

    public static ITypeSymbol? TypeOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        _ => null
    };

    // [WHY] The walk stops at a struct already on its path: a struct that holds itself (CS0523), also a generic one
    // that holds a larger construction of itself, would otherwise be followed without end or k^depth times
    private static void AddStructCopy(
        AliasSources sources,
        AliasKey destination,
        AliasKey source,
        INamedTypeSymbol type,
        StructPath? path)
    {
        using var step = StructPath.Enter(path, type);
        if (step.IsCycle)
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field)
            {
                continue;
            }

            var destinationField = destination.Append(field);
            var sourceField = source.Append(field);
            if (field.Type.TryGetArrayLikeElementType(out _))
            {
                sources.Add(destinationField, AliasSource.FromKey(sourceField));
            }
            else if (field.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } nested)
            {
                AddStructCopy(sources, destinationField, sourceField, nested, step.Path);
            }
        }
    }
}
