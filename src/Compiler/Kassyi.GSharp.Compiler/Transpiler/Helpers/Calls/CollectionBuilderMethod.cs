namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// The method that C# calls for a collection expression converted to a type with <c>[CollectionBuilder]</c>:
/// <c>Coll c = [a, b];</c> calls <c>Builder.Create</c> with a span of <c>a, b</c> and takes its result.
/// </summary>
/// <remarks>
/// [WHY] Roslyn 4.8 has no public form of the builder call (<c>ICollectionExpressionOperation.ConstructMethod</c> came
/// later), so the method is found as the C# specification states it: a static method of the builder type with the given
/// name, as many type parameters as the collection type has type arguments, one <c>ReadOnlySpan&lt;E&gt;</c> parameter
/// passed by value whose <c>E</c> is the iteration type of the collection type, and the collection type as its return
/// type.
/// </remarks>
internal static class CollectionBuilderMethod
{
    private const string GetEnumeratorName = WellKnownMemberNames.GetEnumeratorMethodName;

    private const string CurrentName = WellKnownMemberNames.CurrentPropertyName;

    /// <summary>
    /// The builder type and method name that <c>[CollectionBuilder(typeof(Builder), "Create")]</c> names on
    /// <paramref name="type"/>, or false when the type has no such attribute.
    /// </summary>
    public static bool TryGetDeclaration(ITypeSymbol type, [NotNullWhen(true)] out INamedTypeSymbol? builderType,
        [NotNullWhen(true)] out string? methodName)
    {
        if (type.OriginalDefinition.GetAttribute(KnownAttributes.CollectionBuilder) is
            {
                ConstructorArguments: [{ Value: INamedTypeSymbol builder }, { Value: string name }]
            })
        {
            builderType = builder;
            methodName = name;
            return true;
        }

        builderType = null;
        methodName = null;
        return false;
    }

    /// <summary>
    /// The builder method, constructed with the type arguments of <paramref name="type"/>, that C# calls for a
    /// collection expression converted to <paramref name="type"/>; null when the type has no <c>[CollectionBuilder]</c>
    /// or none of the methods it names applies.
    /// </summary>
    public static IMethodSymbol? Find(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named ||
            !TryGetDeclaration(named, out var builderType, out var methodName) ||
            GetIterationType(named) is not { } element)
        {
            return null;
        }

        return builderType.GetMembers(methodName)
            .AsStackEnumerable()
            .Select((Type: named, Element: element), static (member, target) =>
                AsBuilderMethod(member, target.Type, target.Element)
            ).FirstOrDefault(static method => method is not null);
    }

    // <member> constructed with the type arguments of <type> when it is a builder method of <type> for <element>
    private static IMethodSymbol? AsBuilderMethod(ISymbol member, INamedTypeSymbol type, ITypeSymbol element)
    {
        if (member is not IMethodSymbol { IsStatic: true, Parameters: [{ RefKind: RefKind.None }] } method ||
            method.TypeParameters.Length != type.TypeArguments.Length)
        {
            return null;
        }

        var constructed = method.IsGenericMethod
            ? method.Construct(type.TypeArguments, type.TypeArgumentNullableAnnotations)
            : method;
        return constructed.Parameters[0].Type is INamedTypeSymbol { Name: TypeConstants.ReadOnlySpanType } span &&
               span.ContainingNamespace.IsSystemNamespace() &&
               span.TypeArguments[0].IsEqualTo(element) &&
               constructed.ReturnType.IsEqualTo(type)
            ? constructed
            : null;
    }

    // The type of Current of the GetEnumerator() that foreach would call, or the T of IEnumerable<T>
    private static ITypeSymbol? GetIterationType(INamedTypeSymbol type)
    {
        var getEnumerator = type.GetMembers(GetEnumeratorName)
            .AsStackEnumerable()
            .FirstOrDefault(static member => member is IMethodSymbol { IsStatic: false, Parameters.Length: 0 });
        if (getEnumerator is IMethodSymbol { ReturnType: var enumerator })
        {
            return enumerator.GetMembers(CurrentName)
                .AsStackEnumerable()
                .FirstOrDefault(static member => member is IPropertySymbol) is IPropertySymbol current
                ? current.Type
                : null;
        }

        return type.AllInterfaces
            .AsStackEnumerable()
            .FirstOrDefault(static candidate =>
                candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            ) is { TypeArguments: [var element] }
            ? element
            : null;
    }
}
