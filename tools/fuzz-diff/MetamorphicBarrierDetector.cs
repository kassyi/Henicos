using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Finds barrier, warp, atomic and other G# synchronization calls, directly or through the device functions a node
/// calls, so that no rewrite moves, wraps or duplicates them.
/// </summary>
internal static class MetamorphicBarrierDetector
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="node"/> calls a synchronization or G# intrinsic method, a
    /// method that cannot be resolved, or a method declared in <paramref name="root"/> that does so in turn.
    /// </summary>
    internal static bool ContainsBarrierOrWarpCall(
        SyntaxNode node,
        SemanticModel semanticModel,
        CompilationUnitSyntax root)
    {
        var visitedMethods = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return ContainsBarrierOrWarpCallCore(node, semanticModel, root, visitedMethods);
    }

    private static bool ContainsBarrierOrWarpCallCore(
        SyntaxNode node,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        HashSet<ISymbol> visitedMethods)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is not InvocationExpressionSyntax inv)
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(inv).Symbol is not IMethodSymbol methodSymbol)
            {
                // Fail-closed: a call that cannot be resolved may be a barrier.
                return true;
            }

            if (IsGpuSynchronizationOrIntrinsicSymbol(methodSymbol))
            {
                return true;
            }

            if (visitedMethods.Add(methodSymbol) &&
                CalleeBodyContainsBarrierOrWarpCall(methodSymbol, semanticModel, root, visitedMethods))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CalleeBodyContainsBarrierOrWarpCall(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        HashSet<ISymbol> visitedMethods)
    {
        foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.SyntaxTree == root.SyntaxTree &&
                syntaxRef.GetSyntax() is MethodDeclarationSyntax targetDecl &&
                ((SyntaxNode?)targetDecl.Body ?? targetDecl.ExpressionBody) is { } bodyNode &&
                ContainsBarrierOrWarpCallCore(bodyNode, semanticModel, root, visitedMethods))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGpuSynchronizationOrIntrinsicSymbol(IMethodSymbol methodSymbol)
    {
        string methodName = methodSymbol.Name;
        if (methodName.StartsWith("Sync", StringComparison.Ordinal) ||
            methodName.StartsWith("ThreadFence", StringComparison.Ordinal) ||
            methodName.StartsWith("Shfl", StringComparison.Ordinal) ||
            methodName.StartsWith("Ballot", StringComparison.Ordinal) ||
            methodName.StartsWith("Atomic", StringComparison.Ordinal))
        {
            return true;
        }

        for (INamedTypeSymbol? type = methodSymbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.Name is "Gpu" or "Warp" or "GpuAtomic" or "Interlocked")
            {
                return true;
            }
        }

        return IsInGSharpCoreNamespace(methodSymbol.ContainingNamespace);
    }

    private static bool IsInGSharpCoreNamespace(INamespaceSymbol? ns)
    {
        for (INamespaceSymbol? current = ns; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
        {
            if (string.Equals(current.Name, "Core", StringComparison.Ordinal) &&
                current.ContainingNamespace is { Name: "GSharp", ContainingNamespace.Name: "Kassyi" })
            {
                return true;
            }
        }

        return false;
    }
}
