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

    /// <summary>
    /// Whether the generator leaves out the device function <paramref name="method"/> and the analyzer reports GSH0123
    /// for it: it declares type parameters, names one of them in a parameter type, its return type or its body, and is
    /// not an iterator, whose generic form GSH0070 reports.
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
            leftOut = NamesOwnTypeParameter(definition, compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
            answers.TryAdd(definition, leftOut);
        }

        return leftOut;
    }

    // [WHY] Not cached below the method asked about: a callee is evaluated with the methods on the way to it taken as
    // not left out (a recursive or a mutually recursive call), which is the answer for that path only
    private static bool NamesOwnTypeParameter(IMethodSymbol definition, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        var implementation = definition.PartialImplementationPart ?? definition;
        if (!implementation.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            IteratorTypeResolver.ContainsDirectYield(declaration.Body))
        {
            return false;
        }

        var owners = (definition, implementation);
        if (ContainsOwn(implementation.ReturnType, owners) ||
            implementation.Parameters
                .AsStackEnumerable()
                .Any(owners, static (parameter, o) => ContainsOwn(parameter.Type, o)))
        {
            return true;
        }

        if (declaration.GetBodyNode() is not { } body ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        visiting.Add(definition);
        var context = new BodyContext(compilation.GetCachedSemanticModel(declaration.SyntaxTree), owners, visiting);
        var names = body
            .DescendantNodesAndSelf(context.DescendsInto)
            .AsStackEnumerable()
            .Any(context, static (node, c) => node is ExpressionSyntax expression && c.NamesOwn(expression));
        visiting.Remove(definition);
        return names;
    }

    // The body of a generic method, asked whether it names one of the method's own type parameters
    private sealed class BodyContext(
        SemanticModel model,
        (IMethodSymbol Definition, IMethodSymbol Implementation) owners,
        HashSet<IMethodSymbol> visiting)
    {
        // The type arguments of a call of a generic method that names none of its own type parameters are passed on
        // to nothing (as the fuzzer's DevFunc_3<T0, T1> calls DevFunc_2<T0>): such a call is the same for every type
        // argument, and the callee is emitted once
        public bool DescendsInto(SyntaxNode node) =>
            node is not TypeArgumentListSyntax { Parent: { } name } || !IsPassedToNothing(name);

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
                IMethodSymbol called => !IsPassedToNothing(called) &&
                                        called.TypeArguments
                                            .AsStackEnumerable()
                                            .Any(owners, static (argument, o) => ContainsOwn(argument, o)),
                _ => false,
            };
        }

        private bool IsPassedToNothing(SyntaxNode name) =>
            model.GetSymbolInfo(name).Symbol is IMethodSymbol called && IsPassedToNothing(called);

        // A generic method of the compilation that names none of its own type parameters; a method on the way here is
        // taken as one, and its own answer decides when it is asked about
        private bool IsPassedToNothing(IMethodSymbol called)
        {
            var definition = called.OriginalDefinition;
            return definition.IsGenericMethod &&
                   (visiting.Contains(definition) ||
                    (definition.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out _) &&
                     !NamesOwnTypeParameter(definition, model.Compilation, visiting)));
        }
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
