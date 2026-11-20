namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Classifies whether a <c>[GpuKernel]</c> can run on one native CPU stack per GPU thread instead of a lowered state
/// machine when <c>GSharpNativeBarrierModel=Stack</c> is enabled.
/// </summary>
/// <remarks>
/// [WHY] Each kernel and each device function that suspends is classified, and each walks the device functions it
/// calls: what one body holds is read once per method and compilation (<see cref="s_bodies"/>), and so is the answer for
/// a method (<see cref="s_obstacles"/>), as <see cref="SuspensionAnalysis"/> keeps its answers.
/// </remarks>
internal static class NativeStackEligibility
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, BodyFacts> s_bodies = new();
    private static readonly PerCompilationSymbolCache<IMethodSymbol, NativeStackObstacle> s_obstacles = new();

    // What one body holds, apart from what the device functions it calls hold
    [Flags]
    private enum BodyFacts
    {
        None = 0,
        BlockBarrier = 1,
        WarpCollective = 2,
        GridSync = 4,
        CaseWithoutLabel = 8,
        WarpOperatorMacro = 16
    }

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
        if (semanticModel.GetDeclaredSymbol(declaration) is not IMethodSymbol method)
        {
            return ClassifyClosure(declaration, semanticModel, null);
        }

        var obstacles = s_obstacles.For(semanticModel.Compilation);
        if (obstacles.TryGetValue(method, out var cached))
        {
            return cached;
        }

        var obstacle = ClassifyClosure(declaration, semanticModel, method);
        obstacles.TryAdd(method, obstacle);
        return obstacle;
    }

    // The method's body and the bodies of the device functions it reaches through calls that suspend
    private static NativeStackObstacle ClassifyClosure(MethodDeclarationSyntax declaration,
        SemanticModel semanticModel, IMethodSymbol? method)
    {
        var compilation = semanticModel.Compilation;
        var facts = GetFacts(method, declaration, semanticModel);

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
            facts |= GetFacts(callee, calleeDeclaration, calleeModel);
            EnqueueSuspendingCallees(calleeDeclaration, compilation, visited, queue);
        }

        if ((facts & BodyFacts.GridSync) != 0)
        {
            return NativeStackObstacle.GridSync;
        }

        if ((facts & BodyFacts.WarpOperatorMacro) != 0)
        {
            return NativeStackObstacle.WarpOperatorMacro;
        }

        if ((facts & (BodyFacts.BlockBarrier | BodyFacts.WarpCollective)) != 0 &&
            (facts & BodyFacts.CaseWithoutLabel) != 0)
        {
            return NativeStackObstacle.CaseValueWithoutLabel;
        }

        if ((facts & BodyFacts.WarpCollective) != 0)
        {
            return ClassifyWarpFrame(method, compilation);
        }

        return (facts & BodyFacts.BlockBarrier) != 0 ? NativeStackObstacle.None : NativeStackObstacle.NoBlockBarrier;
    }

    // [WHY] A method that reaches a warp collective runs on stacks only when every collective and every call that
    // reaches one has its place in the visit counts of the kernel, which each GPU thread keeps on its own stack; the
    // stack form is written from that placement and treats a missing place as a compiler bug
    private static NativeStackObstacle ClassifyWarpFrame(IMethodSymbol? method, Compilation compilation)
    {
        if (method is null || !WarpFrameLayout.TryGet(method, compilation, out var layout))
        {
            return NativeStackObstacle.WarpLayout;
        }

        return layout.Size > WarpFrameLayout.MaxSize ? NativeStackObstacle.WarpFrameTooLarge : NativeStackObstacle.None;
    }

    // [WHY] The expansion of an operator macro ([GpuOperatorMacro]) calls its target without the frame of the stack
    // model: WarpFrameLayout places only the calls written as calls, so a target that reaches a warp collective would be
    // called with no offset or not at all. Every overload of the name counts, so the check does not depend on the
    // overload resolution of the expansion. Only a user-defined operator can be a macro, which the symbol of the
    // expression says without building its operation
    private static bool IsWarpOperatorMacro(ExpressionSyntax operatorCall, SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(operatorCall).Symbol is not IMethodSymbol
            {
                MethodKind: MethodKind.UserDefinedOperator
            })
        {
            return false;
        }

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

    // The facts of the body of method, read once per compilation; a declaration without a symbol is read every time
    private static BodyFacts GetFacts(IMethodSymbol? method, MethodDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        if (method is null)
        {
            return ScanBody(declaration, semanticModel);
        }

        var bodies = s_bodies.For(semanticModel.Compilation);
        if (bodies.TryGetValue(method, out var cached))
        {
            return cached;
        }

        var facts = ScanBody(declaration, semanticModel);
        bodies.TryAdd(method, facts);
        return facts;
    }

    private static BodyFacts ScanBody(MethodDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        var facts = BodyFacts.None;
        foreach (var node in RemovedCalls.DescendantNodesThatRun(declaration, semanticModel))
        {
            if (node is BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax &&
                IsWarpOperatorMacro((ExpressionSyntax)node, semanticModel))
            {
                facts |= BodyFacts.WarpOperatorMacro;
                continue;
            }

            if (node is CaseSwitchLabelSyntax label && !SwitchStateLabel.NamesLabel(label.Value, semanticModel))
            {
                facts |= BodyFacts.CaseWithoutLabel;
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

            facts |= intrinsic.Kind switch
            {
                SyncIntrinsicKind.GridBarrier => BodyFacts.GridSync,
                SyncIntrinsicKind.BlockBarrier => BodyFacts.BlockBarrier,
                _ => BodyFacts.WarpCollective
            };
        }

        return facts;
    }
}
