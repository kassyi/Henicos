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
    /// <remarks>A generic type nested in a generic type keeps its type arguments (not followed).</remarks>
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
            case INamedTypeSymbol { IsGenericType: true, ContainingType: not { IsGenericType: true } } named:
                return Substitute(named.TypeArguments) is { } arguments
                    ? named.ConstructedFrom.Construct(arguments)
                    : type;
            default:
                return type;
        }
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
            var member = constructed.GetMembers(method.Name)
                .AsStackEnumerable()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method.OriginalDefinition, static (m, d) => m.OriginalDefinition.IsEqualTo(d));
            if (member == null)
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
