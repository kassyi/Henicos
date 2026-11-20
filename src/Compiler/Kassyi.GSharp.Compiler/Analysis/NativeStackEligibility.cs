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
        var hasActiveMask = false;
        var hasGridSync = false;
        var hasCaseWithoutLabel = false;
        var hasWarpMacro = false;

        ScanBody(declaration, semanticModel, ref hasBlockBarrier, ref hasWarpCollective, ref hasActiveMask,
            ref hasGridSync, ref hasCaseWithoutLabel, ref hasWarpMacro);

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
            ScanBody(calleeDeclaration, calleeModel, ref hasBlockBarrier, ref hasWarpCollective, ref hasActiveMask,
                ref hasGridSync, ref hasCaseWithoutLabel, ref hasWarpMacro);
            EnqueueSuspendingCallees(calleeDeclaration, compilation, visited, queue);
        }

        if (hasGridSync)
        {
            return NativeStackObstacle.GridSync;
        }

        if (hasWarpMacro)
        {
            return NativeStackObstacle.WarpOperatorMacro;
        }

        if ((hasBlockBarrier || hasWarpCollective) && hasCaseWithoutLabel)
        {
            return NativeStackObstacle.CaseValueWithoutLabel;
        }

        if (hasWarpCollective)
        {
            return ClassifyWarpFrame(declaration, semanticModel, hasActiveMask);
        }

        return hasBlockBarrier ? NativeStackObstacle.None : NativeStackObstacle.NoBlockBarrier;
    }

    // [WHY] A method that reaches a warp collective runs on stacks only when every collective and every call that
    // reaches one has its place in the visit counts of the kernel, which each GPU thread keeps on its own stack; the
    // stack form is written from that placement and treats a missing place as a compiler bug
    private static NativeStackObstacle ClassifyWarpFrame(MethodDeclarationSyntax declaration,
        SemanticModel semanticModel, bool hasActiveMask)
    {
        if (hasActiveMask)
        {
            return NativeStackObstacle.WarpActiveMask;
        }

        if (semanticModel.GetDeclaredSymbol(declaration) is not IMethodSymbol method ||
            !WarpFrameLayout.TryGet(method, semanticModel.Compilation, out var layout))
        {
            return NativeStackObstacle.WarpLayout;
        }

        return layout.Size > WarpFrameLayout.MaxSize ? NativeStackObstacle.WarpFrameTooLarge : NativeStackObstacle.None;
    }

    // [WHY] The expansion of an operator macro ([GpuOperatorMacro]) calls its target without the frame of the stack
    // model: WarpFrameLayout places only the calls written as calls, so a target that reaches a warp collective would be
    // called with no offset or not at all. Every overload of the name counts, so the check does not depend on the
    // overload resolution of the expansion
    private static bool IsWarpOperatorMacro(ExpressionSyntax operatorCall, SemanticModel semanticModel)
    {
        var (method, _, _, test) = OperatorMacroSyntax.GetOperator(operatorCall, semanticModel);
        return MacroTargetReachesWarp(method, semanticModel.Compilation) ||
               MacroTargetReachesWarp(test, semanticModel.Compilation);
    }

    private static bool MacroTargetReachesWarp(IMethodSymbol? operatorMethod, Compilation compilation) =>
        OperatorMacroSyntax.GetMacroAttribute(operatorMethod) is { } attribute &&
        attribute.ConstructorArguments[0].Value is INamedTypeSymbol targetType &&
        attribute.ConstructorArguments[1].Value is string targetName &&
        targetType.GetMembers(targetName)
            .AsStackEnumerable()
            .Any(compilation, static (member, c) =>
                member is IMethodSymbol target && SuspensionAnalysis.ReachesWarpShuffle(target, c));

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
        ref bool hasActiveMask,
        ref bool hasGridSync,
        ref bool hasCaseWithoutLabel,
        ref bool hasWarpMacro)
    {
        foreach (var node in RemovedCalls.DescendantNodesThatRun(declaration, semanticModel))
        {
            if (node is BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax &&
                IsWarpOperatorMacro((ExpressionSyntax)node, semanticModel))
            {
                hasWarpMacro = true;
                continue;
            }

            if (node is CaseSwitchLabelSyntax label && !SwitchStateLabel.NamesLabel(label.Value, semanticModel))
            {
                hasCaseWithoutLabel = true;
                continue;
            }

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
                hasActiveMask |= intrinsic.Shuffle == WarpShuffleKind.ActiveMask;
            }
        }
    }
}
