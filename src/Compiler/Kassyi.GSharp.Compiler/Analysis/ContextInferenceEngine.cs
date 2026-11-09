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
    public static bool IsParallelDependent(IMethodSymbol? methodSymbol, ConcurrentDictionary<IMethodSymbol, bool> cache, Compilation compilation)
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

        return EvaluateParallelDependency(methodSymbol, cache, compilation, new EvaluationState());
    }

    // The methods being evaluated, outermost first. MinCutDepth is the shallowest depth of an open method
    // that a call below the current one reached again (a cycle that is still open).
    private sealed class EvaluationState
    {
        public Dictionary<IMethodSymbol, int> Visiting { get; } = new(SymbolEqualityComparer.Default);

        public int MinCutDepth { get; set; } = int.MaxValue;
    }

    private static bool EvaluateParallelDependency(IMethodSymbol methodSymbol, ConcurrentDictionary<IMethodSymbol, bool> cache, Compilation compilation, EvaluationState state)
    {
        if (cache.TryGetValue(methodSymbol, out var cachedResult))
        {
            return cachedResult;
        }

        // A device function of another assembly, called directly or by a function of this one: what its assembly
        // recorded (#538 review m1)
        if (DeviceExportTable.TryFindFunction(methodSymbol, compilation, out var imported))
        {
            cache[methodSymbol] = imported.Record.IsParallelDependent;
            return imported.Record.IsParallelDependent;
        }

        // Cycle detected, break infinite recursion. The false answer depends on the open method, so it is not cached.
        if (state.Visiting.TryGetValue(methodSymbol, out var openDepth))
        {
            state.MinCutDepth = Math.Min(state.MinCutDepth, openDepth);
            return false;
        }

        // Direct check: Does the method symbol belong to Gpu.GridStrideLoop or Gpu.SyncGrid?
        if (GpuMacroClassifier.Classify(methodSymbol).Kind is GpuMacroKind.GridStrideLoop or GpuMacroKind.SyncGrid)
        {
            cache[methodSymbol] = true;
            return true;
        }

        var bodies = GetBodies(methodSymbol, compilation);
        if (bodies.IsEmpty)
        {
            // No source available. Fallback to false.
            cache[methodSymbol] = false;
            return false;
        }

        var depth = state.Visiting.Count;
        var outerMinCutDepth = state.MinCutDepth;
        state.MinCutDepth = int.MaxValue;
        state.Visiting.Add(methodSymbol, depth);
        bool result;
        try
        {
            result = ReachesParallelDependency(bodies, TypeArgumentMap.Of(methodSymbol), cache, compilation, state);
        }
        finally
        {
            state.Visiting.Remove(methodSymbol);
        }

        // A true answer is final. A false one is final only when no open ancestor was cut short below this method.
        var dependsOnOpenAncestor = state.MinCutDepth < depth;
        if (result || !dependsOnOpenAncestor)
        {
            cache[methodSymbol] = result;
        }

        // Cuts to this method itself are closed here; cuts to its ancestors stay open for the caller.
        state.MinCutDepth = Math.Min(outerMinCutDepth, dependsOnOpenAncestor ? state.MinCutDepth : int.MaxValue);
        return result;
    }

    private static bool ReachesParallelDependency(ImmutableArray<IOperation> bodies, TypeArgumentMap typeArguments,
        ConcurrentDictionary<IMethodSymbol, bool> cache, Compilation compilation, EvaluationState state)
    {
        var called = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var body in bodies)
        {
            foreach (var operation in body.DescendantsAndSelf())
            {
                if (IsThreadIntrinsic(operation))
                {
                    return true;
                }

                called.Clear();
                OperationCalls.Add(operation, typeArguments, called);
                foreach (var method in called)
                {
                    if (EvaluateParallelDependency(method, cache, compilation, state))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // ThreadIndex.X, BlockIndex.Y, ...
    private static bool IsThreadIntrinsic(IOperation operation) =>
        operation is IPropertyReferenceOperation
        {
            Property.ContainingType: { Name: "ThreadIndex" or "BlockIndex" } type
        } &&
        type.ContainingNamespace.IsCoreNamespace();

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
            if (definition is not { IsImplicitlyDeclared: true, Parameters.Length: 0, ContainingType.IsValueType: true })
            {
                foreach (var reference in definition.ContainingType.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is TypeDeclarationSyntax declaration)
                    {
                        AddInstanceInitializers(declaration, compilation, bodies);
                    }
                }
            }

            if (definition.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out var constructor))
            {
                AddOperation(constructor, compilation, bodies);
            }

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
