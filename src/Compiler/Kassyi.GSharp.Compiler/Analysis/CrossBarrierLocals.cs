namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds ref structs, <c>ref</c> locals, changed by-value ref struct parameters, <c>foreach (ref ...)</c> variables,
/// stack pointers (GSH0039 / GSH0041), and iterator locals (GSH0072) that cross a state-machine split point.
/// </summary>
internal static class CrossBarrierLocals
{
    public static ImmutableArray<CrossBarrierLocal> Find(MethodDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        if (declaration.Body == null && declaration.ExpressionBody == null)
        {
            return ImmutableArray<CrossBarrierLocal>.Empty;
        }

        var compilation = semanticModel.Compilation;
        var hasSplitPoint = false;
        var trackedLocals = ImmutableArray.CreateBuilder<TrackedLocalInfo>();
        var pointerLocals = ImmutableArray.CreateBuilder<(VariableDeclaratorSyntax Declarator, ILocalSymbol Symbol)>();

        foreach (var node in RemovedCalls.DescendantNodesThatRun(declaration, semanticModel))
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator when
                    KernelConstructChecks.TryClassifyLocalVariableDeclarator(declarator, semanticModel,
                        out var tracked, out var isPointer):
                    if (isPointer)
                    {
                        pointerLocals.Add((declarator, tracked.Symbol));
                    }
                    else
                    {
                        trackedLocals.Add(tracked);
                    }

                    break;
                case InvocationExpressionSyntax invocation when !hasSplitPoint &&
                    semanticModel.GetSymbol(invocation) is IMethodSymbol callee:
                    hasSplitPoint = SyncIntrinsicClassifier.Classify(callee).SplitsStateMachine ||
                                    SuspensionAnalysis.IsSuspendingFunction(callee, compilation);
                    break;
            }
        }

        if (!hasSplitPoint && declaration.GetBodyNode() is { } body)
        {
            hasSplitPoint = DirectCallees.Of([body], TypeArgumentMap.None, compilation)
                .AsStackEnumerable()
                .Any((Model: semanticModel, Compilation: compilation), static (site, state) =>
                    !DirectCallees.IsMethodGroupSite(site.Site, site.Callee, state.Model) &&
                    SuspensionAnalysis.IsSuspendingFunction(site.Callee, state.Compilation));
        }

        if (!hasSplitPoint)
        {
            return ImmutableArray<CrossBarrierLocal>.Empty;
        }

        var flow = new MethodFlowGraph(semanticModel, declaration);
        return FindCore(declaration, semanticModel, flow, trackedLocals, pointerLocals);
    }

    internal static ImmutableArray<CrossBarrierLocal> Find(KernelBarrierScan scan) =>
        scan.HasSplitPoint
            ? FindCore(scan.Method, scan.SemanticModel, scan.Flow, scan.TrackedLocals, scan.PointerLocals)
            : ImmutableArray<CrossBarrierLocal>.Empty;

    private static ImmutableArray<CrossBarrierLocal> FindCore(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        MethodFlowGraph flow,
        ImmutableArray<TrackedLocalInfo>.Builder trackedLocals,
        IReadOnlyList<(VariableDeclaratorSyntax Declarator, ILocalSymbol Symbol)> pointerLocals)
    {
        var results = ImmutableArray.CreateBuilder<CrossBarrierLocal>();
        AddRefForEachAroundSplit(method, semanticModel, results);
        StackPointerProvenance.CollectStackPointerLocals(method, semanticModel, pointerLocals, trackedLocals);

        // The tracked locals, then the by-value ref struct parameters
        var variables = ImmutableArray.CreateBuilder<ISymbol>(trackedLocals.Count);
        foreach (var local in trackedLocals)
        {
            variables.Add(local.Symbol);
        }

        AddTrackedParameters(method, semanticModel, variables);
        if (variables.Count == 0 || !flow.TryGetLiveness(out var graph, out var liveness))
        {
            return results.ToImmutable();
        }

        var liveAcrossSplit = CrossBarrierLiveness.FindLiveAcrossSplit(graph, liveness, variables);
        for (var i = trackedLocals.Count; i < variables.Count; i++)
        {
            if (liveAcrossSplit[i])
            {
                results.Add(new CrossBarrierLocal(
                    variables[i].Locations[0],
                    GSharpDiagnostics.CrossBarrierRefStructProhibited,
                    variables[i].Name));
            }
        }

        for (var i = 0; i < trackedLocals.Count; i++)
        {
            if (!liveAcrossSplit[i])
            {
                continue;
            }

            var tracked = trackedLocals[i];
            if (tracked.IsRefLikeOrRef)
            {
                results.Add(new CrossBarrierLocal(
                    tracked.Declarator.Identifier.GetLocation(),
                    GSharpDiagnostics.CrossBarrierRefStructProhibited,
                    tracked.IdentifierText));
            }
            else if (tracked.IsLocalPointer)
            {
                results.Add(new CrossBarrierLocal(
                    tracked.Declarator.Identifier.GetLocation(),
                    GSharpDiagnostics.CrossBarrierLocalPointerProhibited,
                    tracked.IdentifierText));
            }
            else if (tracked.IsIteratorLocal)
            {
                results.Add(new CrossBarrierLocal(
                    tracked.Declarator.Identifier.GetLocation(),
                    GSharpDiagnostics.IteratorBodyConstructNotSupported,
                    tracked.IdentifierText));
            }
        }

        return results.ToImmutable();
    }

    // GSH0039 at the variable of a foreach (ref ...) whose loop has a barrier or a call that suspends in it: the loop
    // walks its source, a span or a temporary of one, across the split, which the state machine cannot keep in its
    // frame, so the variable is held across the split like a ref local that is live there (#1657)
    private static void AddRefForEachAroundSplit(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        ImmutableArray<CrossBarrierLocal>.Builder results)
    {
        foreach (var node in method.DescendantNodes(static n =>
                     n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is ForEachStatementSyntax loop &&
                RefForEachSplit.IsRefLoop(loop) &&
                RefForEachSplit.HoldsSplitPoint(loop, semanticModel))
            {
                results.Add(new CrossBarrierLocal(
                    loop.Identifier.GetLocation(),
                    GSharpDiagnostics.CrossBarrierRefStructProhibited,
                    loop.Identifier.ValueText));
            }
        }
    }

    // The parameters that the liveness follows (LivenessRecorder.IsTrackedParameter); the location of each is its
    // identifier
    private static void AddTrackedParameters(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel,
        ImmutableArray<ISymbol>.Builder variables)
    {
        if (semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration) is not { } method)
        {
            return;
        }

        method.Parameters
            .AsStackEnumerable()
            .Where(LivenessRecorder.IsTrackedParameter)
            .Select(static ISymbol (p) => p)
            .AddTo(variables);
    }
}
