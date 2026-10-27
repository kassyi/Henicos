namespace Kassyi.GSharp.Compiler.Analysis;

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

        // AST Check: Get Syntax Reference
        if (!methodSymbol.TryGetDeclaringSyntax<SyntaxNode>(out var syntaxNode) ||
            !compilation.ContainsSyntaxTree(syntaxNode.SyntaxTree))
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
            var semanticModel = compilation.GetSemanticModel(syntaxNode.SyntaxTree);
            var walker = new DependencyWalker(semanticModel, cache, compilation, state);
            walker.Visit(syntaxNode);
            result = walker.HasParallelDependency;
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

    private class DependencyWalker(
        SemanticModel semanticModel,
        ConcurrentDictionary<IMethodSymbol, bool> cache,
        Compilation compilation,
        EvaluationState state)
        : CSharpSyntaxWalker
    {
        public bool HasParallelDependency
        {
            get; private set;
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (HasParallelDependency)
            {
                return;
            }

            var symbol = semanticModel.GetSymbol(node);

            // Check for thread intrinsics usage (ThreadIndex.X, BlockIndex.Y, ...)
            if (symbol is IPropertySymbol { ContainingType: { Name: "ThreadIndex" or "BlockIndex" } type } &&
                type.ContainingNamespace.IsCoreNamespace())
            {
                HasParallelDependency = true;
                return;
            }

            // Check property getter dependencies
            if (symbol is IPropertySymbol { GetMethod: not null } propSymbol)
            {
                if (EvaluateParallelDependency(propSymbol.GetMethod, cache, compilation, state))
                {
                    HasParallelDependency = true;
                    return;
                }
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (HasParallelDependency)
            {
                return;
            }

            if (semanticModel.GetSymbol(node) is IMethodSymbol symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, state))
                {
                    HasParallelDependency = true;
                    return;
                }
            }
            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (HasParallelDependency)
            {
                return;
            }

            if (semanticModel.GetSymbol(node) is IMethodSymbol symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, state))
                {
                    HasParallelDependency = true;
                    return;
                }
            }
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (HasParallelDependency)
            {
                return;
            }

            if (semanticModel.GetSymbol(node) is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, state))
                {
                    HasParallelDependency = true;
                    return;
                }
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (HasParallelDependency)
            {
                return;
            }

            if (semanticModel.GetSymbol(node) is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, state))
                {
                    HasParallelDependency = true;
                    return;
                }
            }

            if (semanticModel.GetSymbol(node.Left) is IPropertySymbol { SetMethod: not null } propertySymbol)
            {
                if (EvaluateParallelDependency(propertySymbol.SetMethod, cache, compilation, state))
                {
                    HasParallelDependency = true;
                    return;
                }
            }

            base.VisitAssignmentExpression(node);
        }
    }
}
