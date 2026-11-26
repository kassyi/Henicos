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
    public static bool IsStackFunction(IMethodSymbol function, Compilation compilation) =>
        IsStackFunction(function, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

    private static bool IsStackFunction(IMethodSymbol function, Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        var definition = (function.ReducedFrom ?? function).OriginalDefinition;
        definition = definition.PartialDefinitionPart ?? definition;
        var cache = s_functions.For(compilation);
        if (cache.TryGetValue(definition, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(definition))
        {
            return false;
        }

        bool result;
        try
        {
            result = ComputeIsStackFunction(definition, compilation, visiting);
        }
        finally
        {
            visiting.Remove(definition);
        }

        cache.TryAdd(definition, result);
        return result;
    }

    /// <summary>Whether the managed path can run <paramref name="kernel"/> with one stack per GPU thread.</summary>
    public static bool IsStackKernel(IMethodSymbol kernel, Compilation compilation)
    {
        if (!kernel.IsStatic)
        {
            return false;
        }

        if (GetWarpFailure(kernel, compilation) is not null)
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
        var waits = false;
        foreach (var node in RemovedCalls.DescendantNodesThatRun(body, semanticModel))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            var intrinsic = SyncIntrinsicClassifier.Classify(callee);
            // A thread waits in its body at a block barrier, a grid barrier, a warp collective or a call that reaches one
            if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier
                    or SyncIntrinsicKind.WarpShuffle ||
                SuspensionAnalysis.IsSuspendingFunction(callee, compilation))
            {
                waits = true;
            }
        }

        if (!waits)
        {
            return false;
        }

        foreach (var (callee, _) in
                 SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, semanticModel))
        {
            if (!IsStackFunction(callee, compilation))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Why <paramref name="kernel"/>, which reaches a warp collective (<c>Warp.ActiveMask()</c> included), cannot run
    /// its collectives on the managed stack model, for the build output (<c>GSharp.ManagedStackFailures.g.cs</c>);
    /// <see langword="null"/> when it can, or when it reaches none.
    /// </summary>
    /// <remarks>
    /// [WHY] The stack form finds the visit counts of every collective and call through
    /// <see cref="WarpFrameLayout"/>; a kernel whose counts cannot be placed keeps its state machine here, so that the
    /// stack form never meets a collective without a site (<see cref="ManagedStackWarpLayout"/>).
    /// </remarks>
    public static string? GetWarpFailure(IMethodSymbol kernel, Compilation compilation)
    {
        if (!SuspensionAnalysis.ReachesWarpShuffle(kernel, compilation))
        {
            return null;
        }

        if (!WarpFrameLayout.TryGet(kernel, compilation, out var layout))
        {
            return "the visit counts of its warp collectives cannot be placed (a device function of another " +
                   "assembly or a recursive one reaches a collective)";
        }

        if (layout.Size > WarpFrameLayout.MaxSize)
        {
            return $"its warp collectives need {layout.Size} visit counts per thread, more than " +
                   $"{WarpFrameLayout.MaxSize}";
        }

        if (StackBodyObstacles.DeclaresUsingWithActiveMask(kernel, compilation))
        {
            return "it reaches Warp.ActiveMask() and declares a local with using: a jump of its reconvergence " +
                   "regions may pass the declaration";
        }

        return null;
    }

    private static bool ComputeIsStackFunction(IMethodSymbol function, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(function, compilation, out var declaration) ||
            declaration.GetBodyNode() is not { } body)
        {
            return false;
        }

        if (!SuspensionAnalysis.RequiresStateMachine(function, compilation) ||
            !function.IsStatic ||
            !ManagedStateMachineAvailability.AreContainingTypesPartial(function) ||
            FileLocalNames.BlocksCopy(function, compilation))
        {
            return false;
        }

        if ((SuspensionAnalysis.ReachesWarpShuffle(function, compilation) &&
             !WarpFrameLayout.TryGet(function, compilation, out _)) ||
            StackBodyObstacles.DeclaresUsingWithActiveMask(function, compilation))
        {
            return false;
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        foreach (var (callee, _) in
                 SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, semanticModel))
        {
            if (!IsStackFunction(callee, compilation, visiting))
            {
                return false;
            }
        }

        return true;
    }
}
