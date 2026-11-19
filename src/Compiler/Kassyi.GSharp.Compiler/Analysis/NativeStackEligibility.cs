namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Classifies whether a <c>[GpuKernel]</c> can run on one native CPU stack per GPU thread instead of a lowered state
/// machine when <c>GSharpNativeBarrierModel=Stack</c> is enabled.
/// </summary>
internal static class NativeStackEligibility
{
    public static bool CanRunOnStack(MethodDeclarationSyntax declaration, SemanticModel semanticModel) =>
        declaration.HasBody() &&
        ClassifyBody(declaration, semanticModel) == NativeStackObstacle.None;

    public static NativeStackObstacle Classify(
        IMethodSymbol kernel,
        MethodDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        if (kernel.IsCooperativeKernel())
        {
            return NativeStackObstacle.Cooperative;
        }

        if (!declaration.HasBody())
        {
            return NativeStackObstacle.NoBlockBarrier;
        }

        return ClassifyBody(declaration, semanticModel);
    }

    private static NativeStackObstacle ClassifyBody(
        MethodDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        var compilation = semanticModel.Compilation;
        var hasBlockBarrier = false;
        var hasWarpCollective = false;
        var hasGridSync = false;

        ScanBody(declaration, semanticModel, ref hasBlockBarrier, ref hasWarpCollective, ref hasGridSync);

        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<IMethodSymbol>();
        EnqueueSuspendingCallees(declaration, compilation, visited, queue);

        while (queue.Count > 0)
        {
            var callee = queue.Dequeue();
            if (!SuspensionAnalysis.TryGetDeviceFunctionBody(callee, compilation, out var calleeDeclaration))
            {
                return NativeStackObstacle.ImportedSuspendingCall;
            }

            // Recursive suspending functions (#926, GSH0061) do not get a stack overload; keep them on the
            // diagnostic / state-machine path rather than emitting a broken or no-op barrier call.
            if (!SuspensionAnalysis.RequiresStateMachine(callee, compilation))
            {
                return NativeStackObstacle.RecursiveSuspendingCall;
            }

            var calleeModel = compilation.GetCachedSemanticModel(calleeDeclaration.SyntaxTree);
            ScanBody(calleeDeclaration, calleeModel, ref hasBlockBarrier, ref hasWarpCollective, ref hasGridSync);
            EnqueueSuspendingCallees(calleeDeclaration, compilation, visited, queue);
        }

        if (hasGridSync)
        {
            return NativeStackObstacle.GridSync;
        }

        if (hasWarpCollective)
        {
            return NativeStackObstacle.WarpCollective;
        }

        return hasBlockBarrier ? NativeStackObstacle.None : NativeStackObstacle.NoBlockBarrier;
    }

    private static void EnqueueSuspendingCallees(
        MethodDeclarationSyntax declaration,
        Compilation compilation,
        HashSet<IMethodSymbol> visited,
        Queue<IMethodSymbol> queue)
    {
        foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(declaration, compilation))
        {
            if (visited.Add(callee))
            {
                queue.Enqueue(callee);
            }
        }
    }

    private static void ScanBody(
        MethodDeclarationSyntax declaration,
        SemanticModel semanticModel,
        ref bool hasBlockBarrier,
        ref bool hasWarpCollective,
        ref bool hasGridSync)
    {
        foreach (var node in RemovedCalls.DescendantNodesThatRun(declaration, semanticModel))
        {
            if (node is not InvocationExpressionSyntax invocation ||
                semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            var intrinsic = SyncIntrinsicClassifier.Classify(callee);
            if (!intrinsic.SplitsStateMachine)
            {
                continue;
            }

            if (intrinsic.Kind == SyncIntrinsicKind.GridBarrier)
            {
                hasGridSync = true;
            }
            else if (intrinsic.Kind == SyncIntrinsicKind.BlockBarrier)
            {
                hasBlockBarrier = true;
            }
            else
            {
                hasWarpCollective = true;
            }
        }
    }
}
