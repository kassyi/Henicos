using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Decides whether a method depends on parallel execution: it reaches <c>Gpu.GridStrideLoop</c> or <c>Gpu.SyncGrid</c>,
/// or reads <c>ThreadIndex</c> / <c>BlockIndex</c>, itself or through any method it calls (GSH0035).
/// </summary>
/// <remarks>
/// The calls are read from the operations of the body (<see cref="OperationCalls"/>), so every form that calls a method
/// counts: an invocation, a constructor (also <c>new()</c> and <c>new T()</c>), a property or indexer accessor, a
/// user-defined operator or conversion. A constructor also runs the instance member initializers of its type. A
/// constructed generic member is read as its construction (<see cref="TypeArgumentMap"/>).
/// </remarks>
public static class ContextInferenceEngine
{
    public static bool IsParallelDependent(IMethodSymbol? methodSymbol, ConcurrentDictionary<IMethodSymbol, bool> cache,
        Compilation compilation)
    {
        if (methodSymbol == null)
        {
            return false;
        }

        // Fast path for external framework methods that we cannot traverse; a device function of another assembly is
        // answered by its record (EvaluateParallelDependency)
        if (methodSymbol.ContainingAssembly?.Name != compilation.AssemblyName &&
            methodSymbol.ContainingAssembly?.Name != TypeConstants.CoreNamespace &&
            methodSymbol.ContainingAssembly?.Name != "Kassyi.GSharp.Stdlib.GpuMath" &&
            !DeviceExportTable.TryFindFunction(methodSymbol, compilation, out _))
        {
            return false;
        }

        return EvaluateParallelDependency(methodSymbol, new EvaluationState(cache, compilation));
    }

    // One query: the cache and compilation it answers for, and the methods being evaluated, outermost first, with their
    // depth; _minCutDepth is the shallowest depth of an open method that a call below the current one reached again (a
    // cycle that is still open).
    private sealed class EvaluationState(ConcurrentDictionary<IMethodSymbol, bool> cache, Compilation compilation)
    {
        private readonly Dictionary<IMethodSymbol, int> _visiting = new(SymbolEqualityComparer.Default);

        private int _minCutDepth = int.MaxValue;

        public ConcurrentDictionary<IMethodSymbol, bool> Cache => cache;

        public Compilation Compilation => compilation;

        public ImmutableArray<IMethodSymbol>.Builder Called { get; } = ImmutableArray.CreateBuilder<IMethodSymbol>();

        // A method being evaluated again below itself (a cycle): its false answer depends on the open method
        public bool IsOpen(IMethodSymbol method)
        {
            if (!_visiting.TryGetValue(method, out var openDepth))
            {
                return false;
            }

            _minCutDepth = Math.Min(_minCutDepth, openDepth);
            return true;
        }

        // Opens method; the returned scope is passed to Exit
        public (int Depth, int OuterMinCutDepth) Enter(IMethodSymbol method)
        {
            var scope = (_visiting.Count, _minCutDepth);
            _minCutDepth = int.MaxValue;
            _visiting.Add(method, scope.Count);
            return scope;
        }

        // Closes method and says whether its answer is final: a true answer is, a false one only when no open
        // ancestor was cut short below it. Cuts to the method itself close here; cuts to its ancestors stay open.
        public bool Exit(IMethodSymbol method, (int Depth, int OuterMinCutDepth) scope, bool result)
        {
            _visiting.Remove(method);
            var dependsOnOpenAncestor = _minCutDepth < scope.Depth;
            _minCutDepth = Math.Min(scope.OuterMinCutDepth, dependsOnOpenAncestor ? _minCutDepth : int.MaxValue);
            return result || !dependsOnOpenAncestor;
        }
    }

    private static bool EvaluateParallelDependency(IMethodSymbol methodSymbol, EvaluationState state)
    {
        if (TryGetKnownAnswer(methodSymbol, state, out var known))
        {
            return known;
        }

        // Cycle detected, break infinite recursion. The false answer depends on the open method, so it is not cached.
        if (state.IsOpen(methodSymbol))
        {
            return false;
        }

        // A method without source (or body) answers false. [WHY] The state belongs to one IsParallelDependent query, so
        // an exception (cancellation) needs no cleanup
        var bodies = GetBodies(methodSymbol, state.Compilation);
        var scope = state.Enter(methodSymbol);
        var result = !bodies.IsEmpty && ReachesParallelDependency(bodies, TypeArgumentMap.Of(methodSymbol), state);
        if (state.Exit(methodSymbol, scope, result))
        {
            state.Cache[methodSymbol] = result;
        }

        return result;
    }

    // The answer that needs no walk: a cached one, the record of a device function of another assembly (#538 review
    // m1), or Gpu.GridStrideLoop / Gpu.SyncGrid themselves
    private static bool TryGetKnownAnswer(IMethodSymbol method, EvaluationState state, out bool answer)
    {
        if (state.Cache.TryGetValue(method, out answer))
        {
            return true;
        }

        if (DeviceExportTable.TryFindFunction(method, state.Compilation, out var imported))
        {
            answer = state.Cache[method] = imported.Record.IsParallelDependent;
            return true;
        }

        if (GpuMacroClassifier.Classify(method).Kind is GpuMacroKind.GridStrideLoop or GpuMacroKind.SyncGrid)
        {
            answer = state.Cache[method] = true;
            return true;
        }

        return false;
    }

    private static bool ReachesParallelDependency(ImmutableArray<IOperation> bodies, TypeArgumentMap typeArguments,
        EvaluationState state)
    {
        foreach (var body in bodies)
        {
            if (Reaches(body, typeArguments, state))
            {
                return true;
            }
        }

        return false;
    }

    // Whether operation or an operation that runs inside it reads a thread index or calls a parallel-dependent method;
    // the argument of nameof is not run
    private static bool Reaches(IOperation operation, TypeArgumentMap typeArguments, EvaluationState state)
    {
        if (operation is INameOfOperation)
        {
            return false;
        }

        if (ThreadIntrinsics.IsIndex(operation) || CallsParallelDependentMethod(operation, typeArguments, state))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (Reaches(child, typeArguments, state))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CallsParallelDependentMethod(IOperation operation, TypeArgumentMap typeArguments,
        EvaluationState state)
    {
        // A copy: the evaluation of a callee reuses the shared builder
        var called = state.Called;
        called.Clear();
        OperationCalls.Add(operation, typeArguments, called);
        var methods = called.ToImmutable();
        foreach (var method in methods)
        {
            if (EvaluateParallelDependency(method, state))
            {
                return true;
            }
        }

        return false;
    }

    // The operations that run when the method is called: the body of a method, accessor or local function; for a
    // constructor, its initializer and body and the instance member initializers of its type, except for the implicit
    // parameterless constructor of a struct, which is default(T) and runs nothing. Only syntax of the compilation
    // counts.
    private static ImmutableArray<IOperation> GetBodies(IMethodSymbol method, Compilation compilation)
    {
        var bodies = ImmutableArray.CreateBuilder<IOperation>();
        var definition = method.OriginalDefinition;
        if (definition.MethodKind == MethodKind.Constructor)
        {
            AddConstructorBodies(definition, compilation, bodies);
            return bodies.ToImmutable();
        }

        if (definition.TryGetDeclaringSyntax<SyntaxNode>(out var syntax) &&
            syntax is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or
                ArrowExpressionClauseSyntax)
        {
            AddOperation(syntax, compilation, bodies);
        }

        return bodies.ToImmutable();
    }

    private static void AddConstructorBodies(IMethodSymbol constructor, Compilation compilation,
        ImmutableArray<IOperation>.Builder bodies)
    {
        if (constructor is not { IsImplicitlyDeclared: true, Parameters.Length: 0, ContainingType.IsValueType: true })
        {
            foreach (var reference in constructor.ContainingType.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is TypeDeclarationSyntax declaration)
                {
                    AddInstanceInitializers(declaration, compilation, bodies);
                }
            }
        }

        if (constructor.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out var declared))
        {
            AddOperation(declared, compilation, bodies);
        }
    }

    private static void AddInstanceInitializers(TypeDeclarationSyntax declaration, Compilation compilation,
        ImmutableArray<IOperation>.Builder bodies)
    {
        foreach (var member in declaration.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                                                       !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        AddOperation(variable.Initializer, compilation, bodies);
                    }

                    break;
                case PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.StaticKeyword):
                    AddOperation(property.Initializer, compilation, bodies);
                    break;
            }
        }
    }

    private static void AddOperation(SyntaxNode? syntax, Compilation compilation,
        ImmutableArray<IOperation>.Builder bodies)
    {
        if (syntax != null && compilation.ContainsSyntaxTree(syntax.SyntaxTree) &&
            compilation.GetCachedSemanticModel(syntax.SyntaxTree).GetOperation(syntax) is { } operation)
        {
            bodies.Add(operation);
        }
    }
}
