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
        WarpOperatorMacro = 16,
        // A using declaration in a method that reaches Warp.ActiveMask()
        UsingDeclarationWithActiveMask = 32,
        // A method that reaches Warp.ActiveMask() whose body the stack lowering fails on
        StackBodyLowering = 64,
        // A suspending device function that reaches a warp collective is passed as a method group
        WarpMethodGroup = 128
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
        EnqueueSuspendingCallees(declaration, semanticModel, visited, queue);

        while (queue.Count > 0)
        {
            var callee = queue.Dequeue();
            if (!SuspensionAnalysis.TryGetDeviceFunctionBody(callee, compilation, out var calleeDeclaration))
            {
                return NativeStackObstacle.ImportedSuspendingCall;
            }

            // Recursive suspending functions (#926, GSH0061) do not get a stack overload; keep them on the
            // diagnostic / state-machine path rather than emitting a broken or no-op barrier call.
            if (!SuspensionAnalysis.RequiresStateMachine(callee, compilation) ||
                HasSuspendingCycle(callee, compilation))
            {
                return NativeStackObstacle.RecursiveSuspendingCall;
            }

            var calleeModel = compilation.GetCachedSemanticModel(calleeDeclaration.SyntaxTree);
            facts |= GetFacts(callee, calleeDeclaration, calleeModel);
            EnqueueSuspendingCallees(calleeDeclaration, calleeModel, visited, queue);
        }

        if ((facts & BodyFacts.GridSync) != 0)
        {
            return NativeStackObstacle.GridSync;
        }

        if ((facts & BodyFacts.WarpOperatorMacro) != 0)
        {
            return NativeStackObstacle.WarpOperatorMacro;
        }

        if ((facts & BodyFacts.UsingDeclarationWithActiveMask) != 0)
        {
            return NativeStackObstacle.UsingDeclarationWithActiveMask;
        }

        if ((facts & BodyFacts.StackBodyLowering) != 0)
        {
            return NativeStackObstacle.StackBodyLowering;
        }

        if ((facts & (BodyFacts.BlockBarrier | BodyFacts.WarpCollective)) != 0 &&
            (facts & BodyFacts.CaseWithoutLabel) != 0)
        {
            return NativeStackObstacle.CaseValueWithoutLabel;
        }

        if ((facts & BodyFacts.WarpMethodGroup) != 0)
        {
            return NativeStackObstacle.WarpMethodGroup;
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
        SemanticModel semanticModel,
        HashSet<IMethodSymbol> visited,
        Queue<IMethodSymbol> queue)
    {
        if (declaration.GetBodyNode() is not { } body)
        {
            return;
        }

        foreach (var (callee, _) in
                 SuspensionAnalysis.GetReferencedSuspendingFunctions(body, semanticModel.Compilation, semanticModel))
        {
            if (visited.Add(callee))
            {
                queue.Enqueue(callee);
            }
        }
    }

    private static bool HasSuspendingCycle(IMethodSymbol start, Compilation compilation) =>
        HasSuspendingCycle(
            start,
            compilation,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

    private static bool HasSuspendingCycle(
        IMethodSymbol method,
        Compilation compilation,
        HashSet<IMethodSymbol> visiting,
        HashSet<IMethodSymbol> acyclic)
    {
        if (acyclic.Contains(method))
        {
            return false;
        }

        if (!visiting.Add(method))
        {
            return true;
        }

        try
        {
            if (SuspensionAnalysis.TryGetDeviceFunctionBody(method, compilation, out var declaration) &&
                declaration.GetBodyNode() is { } body)
            {
                var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
                foreach (var (callee, _) in
                         SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, model))
                {
                    if (HasSuspendingCycle(callee, compilation, visiting, acyclic))
                    {
                        return true;
                    }
                }
            }

            acyclic.Add(method);
            return false;
        }
        finally
        {
            visiting.Remove(method);
        }
    }

    // The facts of the body of method, read once per compilation; a declaration without a symbol is read every time
    private static BodyFacts GetFacts(IMethodSymbol? method, MethodDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        if (method is null)
        {
            return ScanBody(declaration, semanticModel) & ~BodyFacts.UsingDeclarationWithActiveMask;
        }

        var bodies = s_bodies.For(semanticModel.Compilation);
        if (bodies.TryGetValue(method, out var cached))
        {
            return cached;
        }

        // [WHY] Only a method that reaches Warp.ActiveMask() gets the reconvergence regions, whose gotos a using
        // declaration would put inside a try (NativeStackObstacle.UsingDeclarationWithActiveMask)
        var facts = ScanBody(declaration, semanticModel);
        if (!SuspensionAnalysis.ReachesActiveMask(method, semanticModel.Compilation))
        {
            facts &= ~BodyFacts.UsingDeclarationWithActiveMask;
        }
        else if (!CanLowerForStack(method, declaration, semanticModel))
        {
            facts |= BodyFacts.StackBodyLowering;
        }

        bodies.TryAdd(method, facts);
        return facts;
    }

    // [WHY] The native stack form of a method that reaches Warp.ActiveMask() writes the body that
    // AstLoweringPipeline.LowerForStack makes; when that lowering fails, the method keeps its state machine, as the
    // managed stack model keeps it (ManagedStackForms), instead of a stack form that fails the native build. The
    // body is lowered once (StackBodies): the stack form writes the same one, and the reason of a failure is stated
    // in the kernel's text (StackBodyFailure)
    private static bool CanLowerForStack(IMethodSymbol method, MethodDeclarationSyntax declaration,
        SemanticModel semanticModel) =>
        !declaration.HasBody() || GetStackBody(method, declaration, semanticModel).Failure is null;

    private static StackBodies.Result GetStackBody(IMethodSymbol method, MethodDeclarationSyntax declaration,
        SemanticModel semanticModel) =>
        StackBodies.Get(declaration, semanticModel, method.OriginalDefinition,
            isDeviceFunction: !method.HasAttribute(KnownAttributes.GpuKernel));

    /// <summary>
    /// Why <paramref name="kernel"/> keeps its state machine with <see cref="NativeStackObstacle.StackBodyLowering"/>:
    /// the method of its closure (the kernel or a device function it reaches through calls that suspend) whose stack
    /// body could not be lowered, and the failure; <see langword="null"/> when none failed.
    /// </summary>
    public static string? StackBodyFailure(IMethodSymbol kernel, MethodDeclarationSyntax declaration,
        SemanticModel semanticModel)
    {
        var compilation = semanticModel.Compilation;
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<(IMethodSymbol Method, MethodDeclarationSyntax Declaration)>();
        queue.Enqueue((kernel, declaration));
        visited.Add(kernel);
        while (queue.Count > 0)
        {
            var (method, methodDeclaration) = queue.Dequeue();
            var model = compilation.GetCachedSemanticModel(methodDeclaration.SyntaxTree);
            if (methodDeclaration.HasBody() && SuspensionAnalysis.ReachesActiveMask(method, compilation) &&
                GetStackBody(method, methodDeclaration, model).Reason is { } reason)
            {
                return MessageText.Name(method) + ": " + reason;
            }

            if (methodDeclaration.GetBodyNode() is { } body)
            {
                foreach (var (callee, _) in
                         SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, model))
                {
                    if (visited.Add(callee) &&
                        SuspensionAnalysis.TryGetDeviceFunctionBody(callee, compilation, out var calleeDeclaration))
                    {
                        queue.Enqueue((callee, calleeDeclaration));
                    }
                }
            }
        }

        return null;
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

            if (StackBodyObstacles.IsUsingDeclaration(node))
            {
                facts |= BodyFacts.UsingDeclarationWithActiveMask;
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

        return facts | ScanWarpMethodGroups(declaration, semanticModel);
    }

    // A suspending function that reaches a warp collective, named as a method group (#2272)
    private static BodyFacts ScanWarpMethodGroups(MethodDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        if (declaration.GetBodyNode() is not { } body)
        {
            return BodyFacts.None;
        }

        var compilation = semanticModel.Compilation;
        foreach (var (referenced, isMethodGroup) in
                 SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, semanticModel))
        {
            if (isMethodGroup && SuspensionAnalysis.ReachesWarpShuffle(referenced, compilation))
            {
                return BodyFacts.WarpMethodGroup;
            }
        }

        return BodyFacts.None;
    }
}
