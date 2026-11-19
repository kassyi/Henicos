using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Eligibility decisions for running kernels and suspending device functions on one OS stack per GPU thread under
/// <c>GSharpManagedBarrierModel=Stack</c>.
/// </summary>
internal static class ManagedStackEligibility
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, bool> s_functions = new();

    /// <summary>Whether the managed path can run <paramref name="function"/> on the stack of its GPU thread.</summary>
    public static bool IsStackFunction(IMethodSymbol function, Compilation compilation)
    {
        var definition = (function.ReducedFrom ?? function).OriginalDefinition;
        definition = definition.PartialDefinitionPart ?? definition;
        var cache = s_functions.For(compilation);
        return cache.GetOrAdd(definition, m => ComputeIsStackFunction(m, compilation));
    }

    /// <summary>Whether the managed path can run <paramref name="kernel"/> with one stack per GPU thread.</summary>
    public static bool IsStackKernel(IMethodSymbol kernel, Compilation compilation)
    {
        if (!kernel.IsStatic || kernel.IsCooperativeKernel())
        {
            return false;
        }

        if (SuspensionAnalysis.ReachesWarpShuffle(kernel, compilation))
        {
            return false;
        }

        if (!kernel.Parameters
                .AsStackEnumerable()
                .All(CSharpExecutionPlan.SupportsStackArgument))
        {
            return false;
        }

        if (!kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree) ||
            declaration.GetBodyNode() is not { } body)
        {
            return false;
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        var hasBlockBarrier = false;
        foreach (var node in RemovedCalls.DescendantNodesThatRun(body, semanticModel))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            var intrinsic = SyncIntrinsicClassifier.Classify(callee);
            if (intrinsic.Kind == SyncIntrinsicKind.GridBarrier)
            {
                return false;
            }

            if (intrinsic.Kind == SyncIntrinsicKind.BlockBarrier ||
                SuspensionAnalysis.IsSuspendingFunction(callee, compilation))
            {
                hasBlockBarrier = true;
            }
        }

        if (!hasBlockBarrier)
        {
            return false;
        }

        return SuspensionAnalysis.GetSuspendingCallees(body, compilation)
            .AsStackEnumerable()
            .All(compilation, static (callee, comp) => IsStackFunction(callee, comp));
    }

    private static bool ComputeIsStackFunction(IMethodSymbol function, Compilation compilation)
    {
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(function, compilation, out var declaration) ||
            declaration.GetBodyNode() is not { } body)
        {
            return false;
        }

        if (!SuspensionAnalysis.RequiresStateMachine(function, compilation) ||
            SuspensionAnalysis.ReachesWarpShuffle(function, compilation) ||
            SuspensionAnalysis.ReachesGridBarrier(function, compilation) ||
            !function.IsStatic ||
            !ManagedStateMachineAvailability.AreContainingTypesPartial(function) ||
            FileLocalNames.BlocksCopy(function, compilation))
        {
            return false;
        }

        return SuspensionAnalysis.GetSuspendingCallees(body, compilation)
            .AsStackEnumerable()
            .All(compilation, static (callee, comp) => IsStackFunction(callee, comp));
    }
}
