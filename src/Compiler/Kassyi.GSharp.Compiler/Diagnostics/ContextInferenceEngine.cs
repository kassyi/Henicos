using System.Collections.Concurrent;

namespace Kassyi.GSharp.Compiler.Diagnostics;

public static class ContextInferenceEngine
{
    public static bool IsParallelDependent(IMethodSymbol? methodSymbol, ConcurrentDictionary<IMethodSymbol, bool> cache, Compilation compilation)
    {
        if (methodSymbol == null)
        {
            return false;
        }

        // Fast path for external framework methods that we cannot traverse
        if (methodSymbol.ContainingAssembly?.Name != compilation.AssemblyName && 
            methodSymbol.ContainingAssembly?.Name != "Kassyi.GSharp.Core" && 
            methodSymbol.ContainingAssembly?.Name != "Kassyi.GSharp.Stdlib.Math")
        {
            return false;
        }

        return EvaluateParallelDependency(methodSymbol, cache, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool EvaluateParallelDependency(IMethodSymbol methodSymbol, ConcurrentDictionary<IMethodSymbol, bool> cache, Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        if (cache.TryGetValue(methodSymbol, out var cachedResult))
        {
            return cachedResult;
        }

        // Cycle detected, break infinite recursion
        if (!visiting.Add(methodSymbol))
        {
            return false;
        }

        // Direct check: Does the method symbol belong to Gpu.GridStrideLoop or Gpu.SyncGrid?
        if (methodSymbol.ContainingType?.Name == "Gpu" && 
            methodSymbol.ContainingType?.ContainingNamespace?.ToString() == "Kassyi.GSharp.Core")
        {
            if (methodSymbol.Name == "GridStrideLoop" || methodSymbol.Name == "SyncGrid")
            {
                cache[methodSymbol] = true;
                return true;
            }
        }



        // AST Check: Get Syntax Reference
        var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
        {
            // No source available. Fallback to false.
            cache[methodSymbol] = false;
            return false;
        }

        var syntaxNode = syntaxRef.GetSyntax();
        var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);

        var walker = new DependencyWalker(semanticModel, cache, compilation, visiting);
        walker.Visit(syntaxNode);

        cache[methodSymbol] = walker.HasParallelDependency;
        return walker.HasParallelDependency;
    }

    private class DependencyWalker(
        SemanticModel semanticModel,
        ConcurrentDictionary<IMethodSymbol, bool> cache,
        Compilation compilation,
        HashSet<IMethodSymbol> visiting)
        : CSharpSyntaxWalker
    {
        public bool HasParallelDependency { get; private set; }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (HasParallelDependency)
            {
                return;
            }

            var symbol = semanticModel.GetSymbolInfo(node).Symbol;
            
            // Check for thread intrinsics usage
            if (symbol is { ContainingType.Name: "Gpu" } && 
                symbol.ContainingType?.ContainingNamespace?.ToString() == "Kassyi.GSharp.Core")
            {
                if (symbol.Name == "ThreadIndex" || symbol.Name == "GlobalThreadIndex" || symbol.Name == "BlockIndex")
                {
                    HasParallelDependency = true;
                    return;
                }
            }

            // Check property getter dependencies
            if (symbol is IPropertySymbol { GetMethod: not null } propSymbol)
            {
                if (EvaluateParallelDependency(propSymbol.GetMethod, cache, compilation, visiting))
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

            if (semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, visiting))
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

            if (semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, visiting))
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

            if (semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, visiting))
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

            if (semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } symbol)
            {
                if (EvaluateParallelDependency(symbol, cache, compilation, visiting))
                {
                    HasParallelDependency = true;
                    return;
                }
            }
            
            if (semanticModel.GetSymbolInfo(node.Left).Symbol is IPropertySymbol { SetMethod: not null } propertySymbol)
            {
                if (EvaluateParallelDependency(propertySymbol.SetMethod, cache, compilation, visiting))
                {
                    HasParallelDependency = true;
                    return;
                }
            }
            
            base.VisitAssignmentExpression(node);
        }
    }
}
