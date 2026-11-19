namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Which calls of a generic definition make a new construction at every turn of a cycle (polymorphic recursion, such as
/// <c>Deep&lt;T&gt;</c> creating <c>Deep&lt;Wrap&lt;T&gt;&gt;</c>), decided as ECMA-335 II.9.2 decides whether a set of
/// generic instantiations is finite.
/// </summary>
/// <remarks>
/// The nodes of the graph are type parameters (a method's own and those of the types that contain it). A call written
/// in a definition's body adds an edge from each type parameter <c>P</c> that occurs in a type argument to the callee's
/// type parameter <c>Q</c> that the argument is for; the edge is expansive when <c>P</c> sits inside another type there
/// (<c>Wrap&lt;P&gt;</c>), not when the argument is <c>P</c> itself. The instantiations are infinite only when an
/// expansive edge lies on a cycle: when <c>Q</c> reaches <c>P</c> again. A constant argument
/// (<c>C&lt;Wrap&lt;int&gt;&gt;</c>) adds no edge, and an expansive edge that no path brings back
/// (<c>D&lt;T&gt;</c> creating <c>E&lt;int&gt;</c>, which creates <c>D&lt;Wrap&lt;U&gt;&gt;</c>) is finite.
/// <see cref="DeviceFunctionReach"/> stops its walk only on such a cycle, and a program with one cannot be emitted
/// anyway (#2199).
/// </remarks>
internal static class GenericExpansion
{
    // [WHY] The edges of each definition, and the callees each definition reaches on a cycle, are found once per
    // compilation; every kernel's walk asks for the same definitions
    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<TypeParameterEdge>> s_edges = new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<IMethodSymbol>> s_expanding = new();

    /// <summary>
    /// The definitions that <paramref name="definition"/> calls through an expansive edge on a cycle of the type
    /// parameters: the calls that make a new construction at every turn.
    /// </summary>
    public static ImmutableArray<IMethodSymbol> GetExpandingCallees(IMethodSymbol definition,
        Compilation compilation) =>
        s_expanding.For(compilation).GetOrAdd(definition, d =>
        {
            var expanding = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var edge in GetEdges(d, compilation))
            {
                if (edge.Expansive && !expanding.Contains(edge.Callee) && Reaches(edge.To, edge.From, compilation))
                {
                    expanding.Add(edge.Callee);
                }
            }

            return expanding.ToImmutableArray();
        });

    // One edge of the graph: the type parameter From occurs in the argument for To in a call of Callee
    private readonly record struct TypeParameterEdge(
        ITypeParameterSymbol From,
        ITypeParameterSymbol To,
        bool Expansive,
        IMethodSymbol Callee);

    // The edges of the calls written in definition's bodies
    private static ImmutableArray<TypeParameterEdge> GetEdges(IMethodSymbol definition, Compilation compilation) =>
        s_edges.For(compilation).GetOrAdd(definition, d =>
        {
            var edges = ImmutableArray.CreateBuilder<TypeParameterEdge>();
            var occurring = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
            foreach (var callee in DeviceFunctionReach.GetWrittenCallees(d, compilation))
            {
                var calleeDefinition = DeviceFunctionEffects.GetDefinition(callee);
                var arguments = GetTypeArguments(callee);
                var parameters = GetTypeParameters(calleeDefinition);
                for (var i = 0; i < arguments.Count && i < parameters.Count; i++)
                {
                    occurring.Clear();
                    AddTypeParameters(arguments[i], occurring);
                    foreach (var from in occurring)
                    {
                        var expansive = !arguments[i].IsEqualTo(from);
                        edges.Add(new TypeParameterEdge(from, parameters[i], expansive, calleeDefinition));
                    }
                }
            }

            return edges.ToImmutable();
        });

    // Whether a path of edges leads from start to target
    private static bool Reaches(ITypeParameterSymbol start, ITypeParameterSymbol target, Compilation compilation)
    {
        var seen = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default) { start };
        var pending = new Stack<ITypeParameterSymbol>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var parameter = pending.Pop();
            if (parameter.IsEqualTo(target))
            {
                return true;
            }

            foreach (var scope in GetScope(parameter))
            {
                foreach (var edge in GetEdges(scope, compilation))
                {
                    if (edge.From.IsEqualTo(parameter) && seen.Add(edge.To))
                    {
                        pending.Push(edge.To);
                    }
                }
            }
        }

        return false;
    }

    // The methods whose bodies can use parameter: its method, or every method of its type and of the types in it
    private static List<IMethodSymbol> GetScope(ITypeParameterSymbol parameter)
    {
        var methods = new List<IMethodSymbol>();
        if (parameter.DeclaringMethod is { } method)
        {
            methods.Add(method.OriginalDefinition);
        }
        else if (parameter.DeclaringType is { } type)
        {
            AddMethods(type.OriginalDefinition, methods);
        }

        return methods;
    }

    private static void AddMethods(INamedTypeSymbol type, List<IMethodSymbol> methods)
    {
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol method:
                    methods.Add(method);
                    break;
                case INamedTypeSymbol nested:
                    AddMethods(nested, methods);
                    break;
            }
        }
    }

    // The type parameters that occur in type, at any depth
    private static void AddTypeParameters(ITypeSymbol type, HashSet<ITypeParameterSymbol> parameters)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                parameters.Add(parameter);
                break;
            case INamedTypeSymbol named:
                for (var current = named; current != null; current = current.ContainingType)
                {
                    foreach (var argument in current.TypeArguments)
                    {
                        AddTypeParameters(argument, parameters);
                    }
                }

                break;
            case IArrayTypeSymbol array:
                AddTypeParameters(array.ElementType, parameters);
                break;
            case IPointerTypeSymbol pointer:
                AddTypeParameters(pointer.PointedAtType, parameters);
                break;
        }
    }

    // The type parameters of the method and of each type that contains it, innermost first
    private static List<ITypeParameterSymbol> GetTypeParameters(IMethodSymbol definition)
    {
        var parameters = new List<ITypeParameterSymbol>(definition.TypeParameters);
        for (var type = definition.ContainingType; type != null; type = type.ContainingType)
        {
            parameters.AddRange(type.TypeParameters);
        }

        return parameters;
    }

    // The type arguments of the method and of each type that contains it, in the order of GetTypeParameters
    private static List<ITypeSymbol> GetTypeArguments(IMethodSymbol function)
    {
        var arguments = new List<ITypeSymbol>(function.TypeArguments);
        for (var type = function.ContainingType; type != null; type = type.ContainingType)
        {
            arguments.AddRange(type.TypeArguments);
        }

        return arguments;
    }
}
