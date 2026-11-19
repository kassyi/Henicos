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
                { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } targetType ||
            targetType.TryGetArrayLikeElementType(out _) ||
            !SymbolEqualityComparer.Default.Equals(targetType, TypeOf(sourceKey.Last)))
        {
            return false;
        }

        if (!destination.Equals(sourceKey))
        {
            AddStructCopy(sources, destination, sourceKey, targetType, 0);
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

    private static void AddStructCopy(
        AliasSources sources,
        AliasKey destination,
        AliasKey source,
        ITypeSymbol type,
        int depth)
    {
        if (depth > 64 || type.TypeKind != TypeKind.Struct || type.SpecialType != SpecialType.None)
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
            else if (field.Type is { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None })
            {
                AddStructCopy(sources, destinationField, sourceField, field.Type, depth + 1);
            }
        }
    }
}
