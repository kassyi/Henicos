namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The generic declarations of device functions that G# does not compile as they are written: a type parameter has no
/// C++ type, and the lowering of a function that reaches a barrier cannot type the fields of its frame. Each instance
/// is to be compiled from its own closed copy instead (<see cref="GenericCopies"/>).
/// </summary>
/// <remarks>
/// Only a declaration that names one of its own type parameters is left out. One whose type parameters appear in
/// neither its signature nor its body means the same for every type argument: it is compiled once, as before. A method
/// that declares no type parameter of its own but is a member of a generic type is not left out either; when it uses
/// the type's parameter, the transpiler reports each use (GSH0003).
/// </remarks>
internal static class GenericDefinitions
{
    /// <summary>The reason that GSH0123 gives for the generic declaration of a device function.</summary>
    public const string NotSupportedReason = "generic device functions are not supported yet";

    private static readonly PerCompilationSymbolCache<IMethodSymbol, bool> s_leftOut = new();

    private static readonly PerCompilationSymbolCache<IMethodSymbol, TypeParameterUses> s_uses = new();

    /// <summary>
    /// Whether the generator leaves out the device function <paramref name="method"/> and the analyzer reports GSH0123
    /// for it: it declares type parameters, names one of them in a parameter type, its return type or its body, and is
    /// not an iterator, whose generic form GSH0070 reports. A type argument of a call to a generic method of the
    /// compilation that is not left out itself does not count.
    /// </summary>
    /// <remarks>
    /// The one answer that the generator, the analyzer and <see cref="ManagedStateMachineAvailability"/> share. A
    /// partial method is read from its implementing declaration.
    /// </remarks>
    public static bool IsLeftOut(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!definition.IsGenericMethod)
        {
            return false;
        }

        var answers = s_leftOut.For(compilation);
        if (!answers.TryGetValue(definition, out var leftOut))
        {
            Solve(definition, compilation, answers);
            answers.TryGetValue(definition, out leftOut);
        }

        return leftOut;
    }

    // [WHY] A least fixed point over the methods reachable from <definition> through calls that pass a type parameter
    // on: a method is left out when it uses one of its own type parameters itself, or passes one to a callee that is
    // left out. A cycle of methods that only pass a type parameter to each other is not left out. Each body is read
    // once per compilation (s_uses); a recursive walk per question re-read a callee at every place it met it and took
    // exponential time on a chain of forwarders
    private static void Solve(IMethodSymbol definition, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, bool> answers)
    {
        var uses = s_uses.For(compilation);
        var reached = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { definition };
        var pending = new Stack<IMethodSymbol>();
        pending.Push(definition);
        while (pending.Count > 0)
        {
            var method = pending.Pop();
            if (answers.ContainsKey(method))
            {
                continue;
            }

            reached.Add(method);
            UsesOf(method, compilation, uses).PassedTo
                .AsStackEnumerable()
                .DoIf(
                    (seen, pending),
                    static (callee, s) => s.seen.Add(callee),
                    static (callee, s) => s.pending.Push(callee)
                );
        }

        var leftOut = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        reached
            .AsStackEnumerable()
            .Do((leftOut, uses), static (method, s) => s.leftOut[method] = s.uses[method].UsesDirectly);

        // Callees are reached after their callers, so a sweep from the end settles a chain at once
        bool changed;
        do
        {
            changed = false;
            for (var i = reached.Count - 1; i >= 0; i--)
            {
                var method = reached[i];
                if (leftOut[method] ||
                    !uses[method].PassedTo
                        .AsStackEnumerable()
                        .Any((leftOut, answers), static (callee, s) =>
                            (s.leftOut.TryGetValue(callee, out var l) && l) ||
                            (s.answers.TryGetValue(callee, out var a) && a)
                        ))
                {
                    continue;
                }

                leftOut[method] = true;
                changed = true;
            }
        }
        while (changed);

        leftOut
            .AsStackEnumerable()
            .Do(answers, static (pair, a) => a.TryAdd(pair.Key, pair.Value));
    }

    private static TypeParameterUses UsesOf(IMethodSymbol definition, Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, TypeParameterUses> uses)
    {
        if (!uses.TryGetValue(definition, out var found))
        {
            found = Collect(definition, compilation);
            uses.TryAdd(definition, found);
        }

        return found;
    }

    // The uses of <definition>'s own type parameters in its signature and body, read once
    private static TypeParameterUses Collect(IMethodSymbol definition, Compilation compilation)
    {
        var implementation = definition.PartialImplementationPart ?? definition;
        if (!implementation.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            IteratorTypeResolver.ContainsDirectYield(declaration.Body))
        {
            return TypeParameterUses.None;
        }

        var owners = (definition, implementation);
        if (ContainsOwn(implementation.ReturnType, owners) ||
            implementation.Parameters
                .AsStackEnumerable()
                .Any(owners, static (parameter, o) => ContainsOwn(parameter.Type, o)))
        {
            return TypeParameterUses.Direct;
        }

        if (declaration.GetBodyNode() is not { } body ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return TypeParameterUses.None;
        }

        var context = new BodyContext(compilation.GetCachedSemanticModel(declaration.SyntaxTree), owners);
        var direct = body
            .DescendantNodesAndSelf(context.DescendsInto)
            .AsStackEnumerable()
            .Any(context, static (node, c) => node is ExpressionSyntax expression && c.NamesOwn(expression));
        return direct ? TypeParameterUses.Direct : new TypeParameterUses(false, context.PassedTo.ToImmutable());
    }

    // What the signature and body of a generic method do with its own type parameters: use one itself, or pass one on
    // as a type argument to the generic methods of the compilation in <PassedTo> (their definitions)
    private sealed class TypeParameterUses(bool usesDirectly, ImmutableArray<IMethodSymbol> passedTo)
    {
        public static readonly TypeParameterUses None = new(false, []);

        public static readonly TypeParameterUses Direct = new(true, []);

        public bool UsesDirectly { get; } = usesDirectly;

        public ImmutableArray<IMethodSymbol> PassedTo { get; } = passedTo;
    }

    // The body of a generic method, asked whether it names one of the method's own type parameters other than as a
    // type argument of a call to a generic method of the compilation; such calls are collected in PassedTo
    private sealed class BodyContext(
        SemanticModel model,
        (IMethodSymbol Definition, IMethodSymbol Implementation) owners)
    {
        private readonly HashSet<IMethodSymbol> _passedTo = new(SymbolEqualityComparer.Default);

        public ImmutableArray<IMethodSymbol>.Builder PassedTo { get; } = ImmutableArray.CreateBuilder<IMethodSymbol>();

        // The type arguments of a call to a generic method of the compilation are passed on, not used here: whether
        // they make this method left out depends on the callee (as the fuzzer's DevFunc_3<T0, T1> calls DevFunc_2<T0>,
        // which names none of its type parameters and is emitted once)
        public bool DescendsInto(SyntaxNode node) =>
            node is not TypeArgumentListSyntax { Parent: { } name } ||
            model.GetSymbolInfo(name).Symbol is not IMethodSymbol called ||
            !IsGenericOfCompilation(called);

        // The expression's symbol, its type or the type it converts to names a type parameter of the method
        public bool NamesOwn(ExpressionSyntax expression)
        {
            var typeInfo = model.GetTypeInfo(expression);
            if ((typeInfo.Type is { } type && ContainsOwn(type, owners)) ||
                (typeInfo.ConvertedType is { } converted && ContainsOwn(converted, owners)))
            {
                return true;
            }

            return model.GetSymbolInfo(expression).Symbol switch
            {
                ITypeSymbol symbolType => ContainsOwn(symbolType, owners),
                IMethodSymbol called => called.TypeArguments
                                            .AsStackEnumerable()
                                            .Any(owners, static (argument, o) => ContainsOwn(argument, o)) &&
                                        !PassesOn(called),
                _ => false,
            };
        }

        // Records a call that passes a type parameter on to a generic method of the compilation
        private bool PassesOn(IMethodSymbol called)
        {
            if (!IsGenericOfCompilation(called))
            {
                return false;
            }

            if (_passedTo.Add(called.OriginalDefinition))
            {
                PassedTo.Add(called.OriginalDefinition);
            }

            return true;
        }

        private static bool IsGenericOfCompilation(IMethodSymbol called) =>
            called.OriginalDefinition is { IsGenericMethod: true } definition &&
            definition.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out _);
    }

    // <type> is, or holds as an element, a pointee or a type argument, a type parameter of the method itself (not one
    // of a containing type or of a local function)
    private static bool ContainsOwn(ITypeSymbol type,
        (IMethodSymbol Definition, IMethodSymbol Implementation) owners) =>
        type switch
        {
            ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method, DeclaringMethod: { } declaring } =>
                declaring.OriginalDefinition.IsEqualTo(owners.Definition) ||
                declaring.OriginalDefinition.IsEqualTo(owners.Implementation),
            IArrayTypeSymbol array => ContainsOwn(array.ElementType, owners),
            IPointerTypeSymbol pointer => ContainsOwn(pointer.PointedAtType, owners),
            INamedTypeSymbol named => named.TypeArguments
                    .AsStackEnumerable()
                    .Any(owners, static (argument, o) => ContainsOwn(argument, o)) ||
                (named.ContainingType is { } containing && ContainsOwn(containing, owners)),
            _ => false,
        };
}
