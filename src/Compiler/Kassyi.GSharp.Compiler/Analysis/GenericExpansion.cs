using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Which calls of a generic definition make a new construction at every turn of a cycle (polymorphic recursion, such as
/// <c>Deep&lt;T&gt;</c> creating <c>Deep&lt;Wrap&lt;T&gt;&gt;</c>), decided as ECMA-335 II.9.2 decides whether a set of
/// generic instantiations is finite.
/// </summary>
/// <remarks>
/// <para>
/// The nodes of the graph are type parameters (a method's own and those of the types that contain it). A call written
/// in a definition's body adds an edge from each type parameter <c>P</c> that occurs in a type argument to the callee's
/// type parameter <c>Q</c> that the argument is for; the edge is expansive when <c>P</c> sits inside another type there
/// (<c>Wrap&lt;P&gt;</c>), not when the argument is <c>P</c> itself. A type argument passed for a type parameter
/// that the callee creates with <c>new Q()</c>, or reaches a member of an interface through (<c>default(Q).Value</c>),
/// itself or through the type parameters it passes it on to, runs a member of that type as well, which adds the edges
/// of a call of it in the caller.
/// </para>
/// <para>
/// The instantiations are infinite only when an expansive edge lies on a cycle: when both of its ends are in one
/// strongly connected component, found once per compilation (Tarjan). A constant argument
/// (<c>C&lt;Wrap&lt;int&gt;&gt;</c>) adds no edge, and an expansive edge that no path brings back
/// (<c>D&lt;T&gt;</c> creating <c>E&lt;int&gt;</c>, which creates <c>D&lt;Wrap&lt;U&gt;&gt;</c>) is finite.
/// <see cref="DeviceFunctionReach"/> stops its walk only on such a cycle, and a program with one cannot be emitted
/// anyway (#2199).
/// </para>
/// </remarks>
internal static class GenericExpansion
{
    // [WHY] Every kernel's walk asks for the same definitions: the edges of each definition, the methods of each type,
    // the type parameters each definition creates or reaches members through, how each type parameter is used and the
    // expanding callees are found once per compilation
    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<TypeParameterEdge>> s_edges = new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol, ImmutableArray<IMethodSymbol>> s_expanding = new();

    private static readonly PerCompilationSymbolCache<INamedTypeSymbol, ImmutableArray<IMethodSymbol>> s_methods =
        new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol,
        ImmutableArray<(ITypeParameterSymbol Parameter, TypeParameterUse Use)>> s_used = new();

    private static readonly PerCompilationSymbolCache<ITypeParameterSymbol, TypeParameterUse> s_use = new();

    private static readonly ConditionalWeakTable<Compilation, Components> s_components = new();

    /// <summary>
    /// The definitions that <paramref name="definition"/> calls through an expansive edge on a cycle of the type
    /// parameters: the calls that make a new construction at every turn.
    /// </summary>
    public static ImmutableArray<IMethodSymbol> GetExpandingCallees(IMethodSymbol definition,
        Compilation compilation) =>
        s_expanding.For(compilation).GetOrAdd(definition, d =>
        {
            var components = s_components.GetValue(compilation, static c => new Components(c));
            var expanding = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            GetEdges(d, compilation)
                .AsStackEnumerable()
                .Where(components, static (edge, graph) => edge.Expansive && graph.AreConnected(edge.From, edge.To))
                .Select(static edge => edge.Callee)
                .AddTo(expanding);
            return expanding.ToImmutableArray();
        });

    // How a generic body runs code of a type argument
    [Flags]
    private enum TypeParameterUse
    {
        None = 0,

        // new Q(): the parameterless constructor of the argument, when it declares one
        Created = 1,

        // A member of an interface reached through Q: the argument's implementation of it
        Reached = 2,
    }

    // One edge of the graph: the type parameter From occurs in the argument for To in a call that the walk takes as a
    // call of Callee (a constructor that a callee runs through new Q() counts as part of the call of that callee)
    private readonly record struct TypeParameterEdge(
        ITypeParameterSymbol From,
        ITypeParameterSymbol To,
        bool Expansive,
        IMethodSymbol Callee);

    // The edges of the calls written in definition's bodies, with the parameterless constructor calls that a callee's
    // new Q() makes of the arguments passed for Q
    private static ImmutableArray<TypeParameterEdge> GetEdges(IMethodSymbol definition, Compilation compilation) =>
        s_edges.For(compilation).GetOrAdd(definition, d =>
        {
            var edges = ImmutableArray.CreateBuilder<TypeParameterEdge>();
            var pending = new Stack<(IMethodSymbol Call, IMethodSymbol Callee)>();
            DeviceFunctionReach.GetWrittenCallees(d, compilation)
                .AsStackEnumerable()
                .Do(pending, static (call, stack) => stack.Push((call, DeviceFunctionEffects.GetDefinition(call))));
            while (pending.Count > 0)
            {
                var (call, callee) = pending.Pop();
                AddCallEdges(call, callee, compilation, edges, pending);
            }

            return edges.ToImmutable();
        });

    // The edges of one call, taken as a call of callee; a constructor that the call runs through new Q() is pushed as
    // part of the same call (its type arguments are parts of this call's, so the pushing ends)
    private static void AddCallEdges(IMethodSymbol call, IMethodSymbol callee, Compilation compilation,
        ImmutableArray<TypeParameterEdge>.Builder edges, Stack<(IMethodSymbol Call, IMethodSymbol Callee)> pending)
    {
        var arguments = GetTypeArguments(call);
        var parameters = GetTypeParameters(DeviceFunctionEffects.GetDefinition(call));
        var occurring = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
        for (var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            occurring.Clear();
            AddTypeParameters(arguments[i], occurring);
            foreach (var from in occurring)
            {
                edges.Add(new TypeParameterEdge(from, parameters[i], !arguments[i].IsEqualTo(from), callee));
            }

            if (arguments[i] is INamedTypeSymbol { TypeKind: TypeKind.Struct } argument &&
                GetUse(parameters[i], compilation) is not TypeParameterUse.None and var use &&
                OperationCalls.FindParameterlessConstructor(argument) is { } constructor &&
                ((use & TypeParameterUse.Reached) != 0 || !constructor.IsImplicitlyDeclared))
            {
                // A member of the argument runs: its type arguments flow as in a call of any member of it
                pending.Push((constructor, callee));
            }
        }
    }

    // How the bodies that can use parameter run code of its type argument: new Q(), or a member of an interface
    // reached through Q; directly or by passing Q on unchanged to a type parameter that does
    private static TypeParameterUse GetUse(ITypeParameterSymbol parameter, Compilation compilation) =>
        s_use.For(compilation).GetOrAdd(parameter, p => FindUse(p, compilation));

    private static TypeParameterUse FindUse(ITypeParameterSymbol parameter, Compilation compilation)
    {
        var use = TypeParameterUse.None;
        var seen = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default) { parameter };
        var pending = new Stack<ITypeParameterSymbol>();
        pending.Push(parameter);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var method in GetScope(current, compilation))
            {
                use |= GetUsedTypeParameters(method, compilation)
                    .AsStackEnumerable()
                    .Where(current, static (used, own) => used.Parameter.IsEqualTo(own))
                    .Aggregate(TypeParameterUse.None, static (all, used) => all | used.Use);
                AddPassedOn(current, method, compilation, seen, pending);
            }
        }

        return use;
    }

    // The type parameters that method passes current on to unchanged (C<current>), not seen before
    private static void AddPassedOn(ITypeParameterSymbol current, IMethodSymbol method, Compilation compilation,
        HashSet<ITypeParameterSymbol> seen, Stack<ITypeParameterSymbol> pending)
    {
        foreach (var callee in DeviceFunctionReach.GetWrittenCallees(method, compilation))
        {
            var arguments = GetTypeArguments(callee);
            var parameters = GetTypeParameters(DeviceFunctionEffects.GetDefinition(callee));
            for (var i = 0; i < arguments.Count && i < parameters.Count; i++)
            {
                if (arguments[i].IsEqualTo(current) && seen.Add(parameters[i]))
                {
                    pending.Push(parameters[i]);
                }
            }
        }
    }

    // The type parameters that method's bodies create with new Q() or reach a member of an interface through
    private static ImmutableArray<(ITypeParameterSymbol Parameter, TypeParameterUse Use)> GetUsedTypeParameters(
        IMethodSymbol method, Compilation compilation) =>
        s_used.For(compilation).GetOrAdd(method, m =>
        {
            var used = ImmutableArray.CreateBuilder<(ITypeParameterSymbol Parameter, TypeParameterUse Use)>();
            if (!DeviceFunctionBodies.TryGet(m, compilation, out var bodies))
            {
                return used.ToImmutable();
            }

            foreach (var body in bodies)
            {
                if (compilation.ContainsSyntaxTree(body.SyntaxTree) &&
                    compilation.GetCachedSemanticModel(body.SyntaxTree).GetOperation(body) is { } operation)
                {
                    AddUses(operation, used);
                }
            }

            return used.ToImmutable();
        });

    // new Q(), a member of an interface reached through Q, and the Dispose of a using of a Q, in operation and the
    // operations inside it
    private static void AddUses(IOperation operation,
        ImmutableArray<(ITypeParameterSymbol Parameter, TypeParameterUse Use)>.Builder used)
    {
        foreach (var node in operation.DescendantsAndSelf())
        {
            if (node is ITypeParameterObjectCreationOperation { Type: ITypeParameterSymbol created })
            {
                used.Add((created, TypeParameterUse.Created));
            }
            else if ((DeviceFunctionReach.GetConstrainedReceiver(node) ?? GetDisposed(node)) is { } reached)
            {
                used.Add((reached, TypeParameterUse.Reached));
            }
        }
    }

    // The type parameter whose Dispose a using of a value of it runs
    private static ITypeParameterSymbol? GetDisposed(IOperation operation) => operation switch
    {
        IUsingOperation { Locals: [{ Type: ITypeParameterSymbol local }, ..] } => local,
        IUsingOperation { Resources.Type: ITypeParameterSymbol resource } => resource,
        IVariableDeclaratorOperation
        {
            Parent.Parent.Parent: IUsingDeclarationOperation, Symbol.Type: ITypeParameterSymbol declared
        } => declared,
        _ => null
    };

    // The edges that leave parameter: those of the methods whose bodies can use it
    private static IEnumerable<ITypeParameterSymbol> GetSuccessors(ITypeParameterSymbol parameter,
        Compilation compilation)
    {
        foreach (var method in GetScope(parameter, compilation))
        {
            foreach (var edge in GetEdges(method, compilation))
            {
                if (edge.From.IsEqualTo(parameter))
                {
                    yield return edge.To;
                }
            }
        }
    }

    // The methods whose bodies can use parameter: its method, or every method of its type and of the types in it
    private static ImmutableArray<IMethodSymbol> GetScope(ITypeParameterSymbol parameter, Compilation compilation) =>
        parameter.DeclaringMethod is { } method
            ? ImmutableArray.Create(method.OriginalDefinition)
            : parameter.DeclaringType is { } type
                ? s_methods.For(compilation).GetOrAdd(type.OriginalDefinition, static t =>
                {
                    var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
                    AddMethods(t, methods);
                    return methods.ToImmutable();
                })
                : ImmutableArray<IMethodSymbol>.Empty;

    private static void AddMethods(INamedTypeSymbol type, ImmutableArray<IMethodSymbol>.Builder methods)
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

    // The strongly connected components of the type-parameter graph of one compilation, found with Tarjan's algorithm
    // from each type parameter the first time it is asked about; a component, once found, never changes
    private sealed class Components(Compilation compilation)
    {
        private readonly object _gate = new();
        private readonly Dictionary<ITypeParameterSymbol, int> _component = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ITypeParameterSymbol, int> _index = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ITypeParameterSymbol, int> _lowLink = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ITypeParameterSymbol> _onStack = new(SymbolEqualityComparer.Default);
        private readonly Stack<ITypeParameterSymbol> _stack = new();
        private int _nextIndex;
        private int _nextComponent;

        // Whether from and to are in one component: whether to reaches from again
        public bool AreConnected(ITypeParameterSymbol from, ITypeParameterSymbol to)
        {
            lock (_gate)
            {
                try
                {
                    return GetComponent(from) == GetComponent(to);
                }
                catch (Exception)
                {
                    // [WHY] A search stopped half way leaves nodes on the stack without a component; later questions
                    // would read that state as finished, so it is dropped and the next question starts afresh
                    Reset();
                    throw;
                }
            }
        }

        private void Reset()
        {
            _component.Clear();
            _index.Clear();
            _lowLink.Clear();
            _onStack.Clear();
            _stack.Clear();
            _nextIndex = 0;
            _nextComponent = 0;
        }

        private int GetComponent(ITypeParameterSymbol parameter)
        {
            if (!_component.ContainsKey(parameter))
            {
                Visit(parameter);
            }

            return _component[parameter];
        }

        // Tarjan's algorithm from root, with an explicit stack of the successors still to visit
        private void Visit(ITypeParameterSymbol root)
        {
            var work = new Stack<(ITypeParameterSymbol Node, IEnumerator<ITypeParameterSymbol> Successors)>();
            Open(root, work);
            while (work.Count > 0)
            {
                var (node, successors) = work.Peek();
                if (successors.MoveNext())
                {
                    var next = successors.Current;
                    if (!_index.ContainsKey(next))
                    {
                        Open(next, work);
                    }
                    else if (_onStack.Contains(next))
                    {
                        _lowLink[node] = Math.Min(_lowLink[node], _index[next]);
                    }

                    continue;
                }

                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    _lowLink[parent] = Math.Min(_lowLink[parent], _lowLink[node]);
                }

                if (_lowLink[node] == _index[node])
                {
                    Close(node);
                }
            }
        }

        private void Open(ITypeParameterSymbol node,
            Stack<(ITypeParameterSymbol Node, IEnumerator<ITypeParameterSymbol> Successors)> work)
        {
            _index[node] = _nextIndex;
            _lowLink[node] = _nextIndex;
            _nextIndex++;
            _stack.Push(node);
            _onStack.Add(node);
            work.Push((node, GetSuccessors(node, compilation).GetEnumerator()));
        }

        // Pops the component whose root is node
        private void Close(ITypeParameterSymbol node)
        {
            ITypeParameterSymbol member;
            do
            {
                member = _stack.Pop();
                _onStack.Remove(member);
                _component[member] = _nextComponent;
            }
            while (!member.IsEqualTo(node));

            _nextComponent++;
        }
    }
}
