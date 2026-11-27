namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The type arguments of a constructed method and of the types that contain it, by type parameter, to read the generic
/// declaration that the method was constructed from as the construction.
/// </summary>
/// <remarks>
/// [WHY] The body of the constructor of <c>HT&lt;PTid&gt;</c> is the syntax of <c>HT&lt;T&gt;</c>, where <c>new T()</c>
/// binds to no constructor and a call that passes <c>T</c> on binds to a member of the definition; the map turns both
/// into what the construction runs (#953).
/// </remarks>
internal readonly struct TypeArgumentMap
{
    private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol>? _map;

    private TypeArgumentMap(Dictionary<ITypeParameterSymbol, ITypeSymbol> map) => _map = map;

    /// <summary>No type parameter is replaced.</summary>
    public static TypeArgumentMap None => default;

    /// <summary>The type arguments of <paramref name="method"/> and of each type that contains it.</summary>
    public static TypeArgumentMap Of(IMethodSymbol method)
    {
        Dictionary<ITypeParameterSymbol, ITypeSymbol>? map = null;
        Add(ref map, method.TypeParameters, method.TypeArguments);
        // [WHY] A constructed partial method symbol in Roslyn has PartialImplementationPart == null, so its body's
        // syntax binds its own type parameters to OriginalDefinition.PartialImplementationPart.TypeParameters.
        if (method.OriginalDefinition.PartialImplementationPart is { } implementation)
        {
            Add(ref map, implementation.TypeParameters, method.TypeArguments);
        }

        if (method.OriginalDefinition.PartialDefinitionPart is { } definition)
        {
            Add(ref map, definition.TypeParameters, method.TypeArguments);
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            Add(ref map, type.TypeParameters, type.TypeArguments);
        }

        return map == null ? None : new TypeArgumentMap(map);
    }

    private static void Add(ref Dictionary<ITypeParameterSymbol, ITypeSymbol>? map,
        ImmutableArray<ITypeParameterSymbol> parameters, ImmutableArray<ITypeSymbol> arguments)
    {
        for (var i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            // The definition itself maps each parameter to itself: nothing to replace
            if (arguments[i].IsEqualTo(parameters[i].OriginalDefinition))
            {
                continue;
            }

            map ??= new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
            map[parameters[i].OriginalDefinition] = arguments[i];
        }
    }

    /// <summary><paramref name="type"/> with the mapped type parameters replaced; itself when none is mapped.</summary>
    /// <remarks>Generic types nested in generic types also replace enclosing and own type arguments.</remarks>
    public ITypeSymbol Substitute(ITypeSymbol type)
    {
        if (_map == null)
        {
            return type;
        }

        switch (type)
        {
            case ITypeParameterSymbol parameter:
                return _map.TryGetValue(parameter.OriginalDefinition, out var argument) ? argument : type;
            case INamedTypeSymbol { IsGenericType: true, IsUnboundGenericType: false } named:
                return SubstituteNamed(named);
            default:
                return type;
        }
    }

    private INamedTypeSymbol SubstituteNamed(INamedTypeSymbol named)
    {
        var containing = named.ContainingType is { IsGenericType: true } outer
            ? Substitute(outer) as INamedTypeSymbol
            : null;
        var containingChanged = containing != null && !containing.IsEqualTo(named.ContainingType);
        var arguments = Substitute(named.TypeArguments);
        if (!containingChanged && arguments == null)
        {
            return named;
        }

        var definition = named.ConstructedFrom;
        if (containing != null && containingChanged)
        {
            definition = containing.GetTypeMembers(named.Name, named.Arity)
                .AsStackEnumerable()
                .FirstOrDefault(named.OriginalDefinition,
                    static (t, d) => t.OriginalDefinition.IsEqualTo(d)) ?? definition;
        }

        return named.Arity == 0
            ? definition
            : definition.Construct(arguments ?? Copy(named.TypeArguments));
    }

    /// <summary>
    /// <paramref name="method"/>, a member of the generic declaration being walked, as the member of the construction:
    /// with its containing type and its own type arguments replaced. Itself when nothing in it is mapped.
    /// </summary>
    public IMethodSymbol Substitute(IMethodSymbol method)
    {
        if (_map == null)
        {
            return method;
        }

        var result = method;
        if (method.ContainingType is { IsGenericType: true } containing &&
            Substitute(containing) is INamedTypeSymbol constructed && !constructed.IsEqualTo(containing))
        {
            if (FindConstructedMember(constructed, method) is not { } member)
            {
                return method;
            }

            result = member;
        }

        // The member of the constructed type is the unconstructed method again: construct it with the type arguments
        if (method.IsGenericMethod)
        {
            var arguments = Substitute(method.TypeArguments);
            if (arguments != null || !ReferenceEquals(result, method))
            {
                result = result.ConstructedFrom.Construct(arguments ?? Copy(method.TypeArguments));
            }
        }

        return result;
    }

    // The member of constructed that method, a member of another construction of the same type, stands for
    private static IMethodSymbol? FindConstructedMember(INamedTypeSymbol constructed, IMethodSymbol method)
    {
        var definition = method.OriginalDefinition.PartialDefinitionPart ?? method.OriginalDefinition;
        return constructed.GetMembers(definition.Name)
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(definition, static (m, d) => m.OriginalDefinition.IsEqualTo(d));
    }

    private static ITypeSymbol[] Copy(ImmutableArray<ITypeSymbol> types)
    {
        var copy = new ITypeSymbol[types.Length];
        types.CopyTo(copy);
        return copy;
    }

    // The types with the mapped type parameters replaced, or null when none changes
    private ITypeSymbol[]? Substitute(ImmutableArray<ITypeSymbol> types)
    {
        ITypeSymbol[]? result = null;
        for (var i = 0; i < types.Length; i++)
        {
            var substituted = Substitute(types[i]);
            if (result == null && !substituted.IsEqualTo(types[i]))
            {
                result = new ITypeSymbol[types.Length];
                for (var j = 0; j < i; j++)
                {
                    result[j] = types[j];
                }
            }

            if (result != null)
            {
                result[i] = substituted;
            }
        }

        return result;
    }
}
