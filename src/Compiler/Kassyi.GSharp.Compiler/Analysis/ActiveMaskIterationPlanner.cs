using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Builds <see cref="ActiveMaskIterationPlan"/> for a kernel or device function body on CUDA.
/// </summary>
internal static class ActiveMaskIterationPlanner
{
    public static ActiveMaskIterationPlan Plan(
        MethodDeclarationSyntax declaration,
        SemanticModel semanticModel,
        IMethodSymbol method,
        bool isDeviceFunction)
    {
        if (declaration.Body is not { } body)
        {
            return ActiveMaskIterationPlan.Empty;
        }

        var activeMaskCalls = CollectDirectActiveMaskCalls(body, semanticModel);
        if (activeMaskCalls.Length == 0)
        {
            return ActiveMaskIterationPlan.Empty;
        }

        var callSet = new HashSet<InvocationExpressionSyntax>(activeMaskCalls);
        bool HoldsSplitPoint(StatementSyntax statement) =>
            statement.DescendantNodesAndSelf(static n => !IsNestedCallable(n))
                .AsStackEnumerable()
                .Any(callSet, static (node, set) =>
                    node is InvocationExpressionSyntax invocation && set.Contains(invocation));

        var cyclePlan = CyclePlanner.Plan(
            body,
            HoldsSplitPoint,
            condition => semanticModel.GetConstantCondition(condition),
            semanticModel,
            enterScopedStatements: true);

        var loops = CollectLoops(body, cyclePlan, HoldsSplitPoint, semanticModel);
        if (loops.Length == 0)
        {
            return ActiveMaskIterationPlan.Empty;
        }

        var heldLoopsPerCall =
            ImmutableArray.CreateBuilder<(InvocationExpressionSyntax Call, ImmutableArray<LoopCandidate> Held)>(
                activeMaskCalls.Length);
        var anyHeldLoop = false;
        foreach (var call in activeMaskCalls)
        {
            var held = ComputeHeldLoops(call, loops);
            if (held.Length > 0)
            {
                anyHeldLoop = true;
            }

            heldLoopsPerCall.Add((call, held));
        }

        if (!anyHeldLoop)
        {
            return ActiveMaskIterationPlan.Empty;
        }

        var uniformity = TryGetUniformity(declaration, semanticModel, isDeviceFunction);
        var targetedLabels = CollectTargetedLabels(body, semanticModel);
        var keyedByLoop = new Dictionary<LoopCandidate, IterationKey>();
        var keyedLoopsBuilder = ImmutableArray.CreateBuilder<KeyedLoop>();
        var resetBuilders = new Dictionary<StatementSyntax, ImmutableArray<int>.Builder>();
        var postLabelResetBuilders = new Dictionary<LabeledStatementSyntax, ImmutableArray<int>.Builder>();
        var switchResetBuilders = new Dictionary<SwitchStatementSyntax, ImmutableArray<int>.Builder>();
        var loopCounters = new Dictionary<StatementSyntax, int>();
        var labelCounterBuilders = new Dictionary<LabeledStatementSyntax, ImmutableArray<int>.Builder>();
        var switchSectionCounters = new Dictionary<SwitchSectionSyntax, int>();
        var counterCount = 0;

        foreach (var loop in loops)
        {
            if (!IsHeldByAnyCall(loop, heldLoopsPerCall) ||
                ActiveMaskLoopUniformity.IsUniformLoop(loop, uniformity, semanticModel))
            {
                continue;
            }

            if (loop.StructuredLoop is ForStatementSyntax forLoop &&
                CountedLoopKeys.TryGetCountedLoopVariable(forLoop, body, semanticModel, uniformity, out var loopVariable))
            {
                keyedByLoop[loop] = IterationKey.ForLoopVariable(loopVariable);
                keyedLoopsBuilder.Add(new KeyedLoop(
                    loop.HeaderStatement,
                    IterationKeyKind.LoopVariable,
                    -1,
                    loopVariable,
                    ResetStatement: null,
                    IncrementLabel: null));
            }
            else
            {
                var counterIndex = counterCount++;
                keyedByLoop[loop] = IterationKey.ForCounter(counterIndex);
                StatementSyntax? resetStatement = null;
                LabeledStatementSyntax? incrementLabel = null;

                if (loop.SwitchOwner != null && loop.SwitchSection != null)
                {
                    AddCounterToBuilder(switchResetBuilders, loop.SwitchOwner, counterIndex);
                    resetStatement = loop.SwitchOwner;
                    if (loop.IncrementSwitchSection)
                    {
                        switchSectionCounters[loop.SwitchSection] = counterIndex;
                    }

                    if (!loop.SwitchIncrementLabels.IsDefaultOrEmpty)
                    {
                        incrementLabel = loop.SwitchIncrementLabels[0];
                        foreach (var lbl in loop.SwitchIncrementLabels)
                        {
                            AddCounterToBuilder(labelCounterBuilders, lbl, counterIndex);
                        }
                    }
                }
                else if (loop.GotoOwner != null)
                {
                    foreach (var stmt in loop.PreResetStatements)
                    {
                        AddCounterToBuilder(resetBuilders, stmt, counterIndex);
                    }

                    foreach (var lbl in loop.PostLabelResetLabels)
                    {
                        AddCounterToBuilder(postLabelResetBuilders, lbl, counterIndex);
                    }

                    if (loop.PreResetStatements.Length > 0)
                    {
                        resetStatement = loop.PreResetStatements[0];
                    }
                    else if (loop.PostLabelResetLabels.Length > 0)
                    {
                        resetStatement = loop.PostLabelResetLabels[0];
                    }

                    incrementLabel = loop.IncrementLabel;
                    if (incrementLabel != null)
                    {
                        AddCounterToBuilder(labelCounterBuilders, incrementLabel, counterIndex);
                    }
                }
                else if (loop.StructuredLoop != null)
                {
                    IterationCounterResets.FindStructuredResetPlacement(
                        loop.StructuredLoop,
                        semanticModel,
                        targetedLabels,
                        out var preReset,
                        out var postLabelReset);
                    if (postLabelReset != null)
                    {
                        AddCounterToBuilder(postLabelResetBuilders, postLabelReset, counterIndex);
                        resetStatement = postLabelReset;
                    }
                    else if (preReset != null)
                    {
                        AddCounterToBuilder(resetBuilders, preReset, counterIndex);
                        resetStatement = preReset;
                    }

                    loopCounters[loop.StructuredLoop] = counterIndex;
                }

                keyedLoopsBuilder.Add(new KeyedLoop(
                    loop.HeaderStatement,
                    IterationKeyKind.Counter,
                    counterIndex,
                    LoopVariable: null,
                    resetStatement,
                    incrementLabel));
            }
        }

        if (keyedLoopsBuilder.Count == 0)
        {
            return ActiveMaskIterationPlan.Empty;
        }

        var callKeys = new Dictionary<InvocationExpressionSyntax, ImmutableArray<IterationKey>>();
        var callKeysBySpan = new Dictionary<TextSpan, ImmutableArray<IterationKey>>();
        foreach (var (call, held) in heldLoopsPerCall)
        {
            var builder = ImmutableArray.CreateBuilder<IterationKey>();
            foreach (var loop in held)
            {
                if (keyedByLoop.TryGetValue(loop, out var key))
                {
                    builder.Add(key);
                }
            }

            if (builder.Count > 0)
            {
                var keys = builder.ToImmutable();
                callKeys[call] = keys;
                callKeysBySpan[call.Span] = keys;
            }
        }

        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(
            semanticModel.Compilation,
            declaration.SyntaxTree);

        return new ActiveMaskIterationPlan(
            keyedLoopsBuilder.ToImmutable(),
            counterCount,
            treeOrdinal,
            callKeys,
            callKeysBySpan,
            FreezeCounterMap(resetBuilders),
            FreezeCounterMap(postLabelResetBuilders),
            FreezeCounterMap(switchResetBuilders),
            loopCounters,
            FreezeCounterMap(labelCounterBuilders),
            switchSectionCounters);
    }

    private static void AddCounterToBuilder<TKey>(
        Dictionary<TKey, ImmutableArray<int>.Builder> builders,
        TKey key,
        int counterIndex) where TKey : notnull
    {
        if (!builders.TryGetValue(key, out var list))
        {
            list = ImmutableArray.CreateBuilder<int>();
            builders[key] = list;
        }

        if (!list.Contains(counterIndex))
        {
            list.Add(counterIndex);
        }
    }

    private static Dictionary<TKey, ImmutableArray<int>> FreezeCounterMap<TKey>(
        Dictionary<TKey, ImmutableArray<int>.Builder> builders) where TKey : notnull
    {
        var result = new Dictionary<TKey, ImmutableArray<int>>(builders.Count);
        foreach (var pair in builders)
        {
            result[pair.Key] = pair.Value.ToImmutable();
        }

        return result;
    }

    private static HashSet<LabeledStatementSyntax> CollectTargetedLabels(
        BlockSyntax body,
        SemanticModel semanticModel)
    {
        var targeted = new HashSet<LabeledStatementSyntax>();
        foreach (var node in RemovedCalls.DescendantNodesThatRun(
                     body,
                     semanticModel,
                     static (n, _) => IsNestedCallable(n)))
        {
            if (node is GotoStatementSyntax gotoStmt &&
                gotoStmt.IsKind(SyntaxKind.GotoStatement) &&
                gotoStmt.Expression is IdentifierNameSyntax { Identifier.ValueText: var labelName } &&
                !labelName.StartsWith(GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal) &&
                ReconvergenceSyntax.FindLabel(gotoStmt, labelName) is { } targetLabel)
            {
                targeted.Add(targetLabel);
            }
        }

        return targeted;
    }

    private static ImmutableArray<InvocationExpressionSyntax> CollectDirectActiveMaskCalls(
        BlockSyntax body,
        SemanticModel semanticModel)
    {
        var calls = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();
        foreach (var node in RemovedCalls.DescendantNodesThatRun(
                     body,
                     semanticModel,
                     static (n, _) => IsNestedCallable(n)))
        {
            if (node is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbol(invocation) is IMethodSymbol callee &&
                SyncIntrinsicClassifier.IsWarpActiveMask(callee))
            {
                calls.Add(invocation);
            }
        }

        return calls.ToImmutable();
    }

    internal static bool IsNestedCallable(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static UniformityAnalysis? TryGetUniformity(
        MethodDeclarationSyntax declaration,
        SemanticModel semanticModel,
        bool isDeviceFunction)
    {
        var flowGraph = new MethodFlowGraph(semanticModel, declaration);
        var masks = isDeviceFunction ? new ArgumentMasks(ulong.MaxValue, 0) : default;
        return flowGraph.TryGetUniformity(masks, out var uniformity) ? uniformity : null;
    }

    private static bool IsHeldByAnyCall(
        LoopCandidate loop,
        ImmutableArray<(InvocationExpressionSyntax Call, ImmutableArray<LoopCandidate> Held)>.Builder heldLoopsPerCall)
    {
        foreach (var (_, held) in heldLoopsPerCall)
        {
            if (held.Contains(loop))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<LoopCandidate> CollectLoops(
        BlockSyntax body,
        CyclePlan cyclePlan,
        Func<StatementSyntax, bool> holdsSplitPoint,
        SemanticModel semanticModel)
    {
        var loops = ImmutableArray.CreateBuilder<LoopCandidate>();
        StatementFlow? switchFlow = null;
        CollectFromList(
            body.Statements,
            body,
            body,
            cyclePlan,
            holdsSplitPoint,
            semanticModel,
            ref switchFlow,
            loops);
        return loops.ToImmutable();
    }

    private static void CollectFromList(
        SyntaxList<StatementSyntax> statements,
        SyntaxNode owner,
        BlockSyntax methodBody,
        CyclePlan cyclePlan,
        Func<StatementSyntax, bool> holdsSplitPoint,
        SemanticModel semanticModel,
        ref StatementFlow? switchFlow,
        ImmutableArray<LoopCandidate>.Builder loops)
    {
        var jumpPlan = JumpRegionPlanner.Plan(statements, holdsSplitPoint);
        Dictionary<int, ImmutableArray<JumpRegion>.Builder>? gotoLoopsByFirst = null;
        int[]? canonicalIndices = null;
        ImmutableArray<(int K, int L)>.Builder? forwardEdges = null;
        ImmutableArray<(int K, int L)>.Builder? backwardEdges = null;

        if (!jumpPlan.Regions.IsDefaultOrEmpty)
        {
            foreach (var region in jumpPlan.Regions)
            {
                if (region.Kind != JumpRegionKind.GotoLoop ||
                    (uint)region.First >= (uint)statements.Count ||
                    (uint)region.Last >= (uint)statements.Count ||
                    region.First > region.Last)
                {
                    continue;
                }

                gotoLoopsByFirst ??= new Dictionary<int, ImmutableArray<JumpRegion>.Builder>();
                if (!gotoLoopsByFirst.TryGetValue(region.First, out var atFirst))
                {
                    atFirst = ImmutableArray.CreateBuilder<JumpRegion>();
                    gotoLoopsByFirst[region.First] = atFirst;
                }

                atFirst.Add(region);
            }
        }

        if (gotoLoopsByFirst != null)
        {
            canonicalIndices = JumpRegionPlanner.BuildCanonicalLabelIndices(statements);
            IterationCounterResets.CollectListGotoEdges(statements, canonicalIndices, out forwardEdges, out backwardEdges);
        }

        for (var i = 0; i < statements.Count; i++)
        {
            if (gotoLoopsByFirst != null &&
                canonicalIndices != null &&
                forwardEdges != null &&
                backwardEdges != null &&
                gotoLoopsByFirst.TryGetValue(i, out var atFirst))
            {
                foreach (var region in atFirst)
                {
                    var header = statements[region.First];
                    var cycle = cyclePlan.Find(statements, region);
                    var incrementLabel = FindIncrementLabel(statements, region.LabelIndex);
                    IterationCounterResets.ComputeGotoLoopResetTargets(
                        statements,
                        region.First,
                        region.Last,
                        canonicalIndices,
                        forwardEdges,
                        backwardEdges,
                        semanticModel,
                        out var preResetStatements,
                        out var postLabelResetLabels);
                    loops.Add(LoopCandidate.ForGotoLoop(
                        header,
                        statements,
                        owner,
                        region.First,
                        region.Last,
                        cyclePlan,
                        cycle,
                        incrementLabel,
                        preResetStatements,
                        postLabelResetLabels));
                }
            }

            CollectFromStatement(
                statements[i],
                methodBody,
                cyclePlan,
                holdsSplitPoint,
                semanticModel,
                ref switchFlow,
                loops);
        }
    }

    private static void CollectFromStatement(
        StatementSyntax statement,
        BlockSyntax methodBody,
        CyclePlan cyclePlan,
        Func<StatementSyntax, bool> holdsSplitPoint,
        SemanticModel semanticModel,
        ref StatementFlow? switchFlow,
        ImmutableArray<LoopCandidate>.Builder loops)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        switch (statement)
        {
            case BlockSyntax block:
                CollectFromList(
                    block.Statements,
                    block,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case CheckedStatementSyntax checkedStatement:
                CollectFromList(
                    checkedStatement.Block.Statements,
                    checkedStatement.Block,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case UnsafeStatementSyntax unsafeStatement:
                CollectFromList(
                    unsafeStatement.Block.Statements,
                    unsafeStatement.Block,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case UsingStatementSyntax usingStatement:
                CollectFromStatement(
                    usingStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case FixedStatementSyntax fixedStatement:
                CollectFromStatement(
                    fixedStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case LockStatementSyntax lockStatement:
                CollectFromStatement(
                    lockStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case IfStatementSyntax ifStatement:
                CollectFromStatement(
                    ifStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                if (ifStatement.Else is { } elseClause)
                {
                    CollectFromStatement(
                        elseClause.Statement,
                        methodBody,
                        cyclePlan,
                        holdsSplitPoint,
                        semanticModel,
                        ref switchFlow,
                        loops);
                }

                break;
            case WhileStatementSyntax whileStatement:
                AddStructuredLoopIfNatural(whileStatement, whileStatement.Statement, cyclePlan, loops);
                CollectFromStatement(
                    whileStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case DoStatementSyntax doStatement:
                AddStructuredLoopIfNatural(doStatement, doStatement.Statement, cyclePlan, loops);
                CollectFromStatement(
                    doStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case ForStatementSyntax forStatement:
                AddStructuredLoopIfNatural(forStatement, forStatement.Statement, cyclePlan, loops);
                CollectFromStatement(
                    forStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case CommonForEachStatementSyntax forEachStatement:
                if (IteratorTypeResolver.TryResolveIteratorSource(
                        forEachStatement.Expression,
                        semanticModel,
                        out _,
                        out _))
                {
                    return;
                }

                AddStructuredLoopIfNatural(forEachStatement, forEachStatement.Statement, cyclePlan, loops);
                CollectFromStatement(
                    forEachStatement.Statement,
                    methodBody,
                    cyclePlan,
                    holdsSplitPoint,
                    semanticModel,
                    ref switchFlow,
                    loops);
                break;
            case SwitchStatementSyntax switchStatement:
                SwitchSectionCycles.CollectSwitchSectionCycles(switchStatement, methodBody, semanticModel, ref switchFlow, loops);
                foreach (var section in switchStatement.Sections)
                {
                    CollectFromList(
                        section.Statements,
                        section,
                        methodBody,
                        cyclePlan,
                        holdsSplitPoint,
                        semanticModel,
                        ref switchFlow,
                        loops);
                }

                break;
        }
    }

    private static void AddStructuredLoopIfNatural(
        StatementSyntax loop,
        StatementSyntax body,
        CyclePlan cyclePlan,
        ImmutableArray<LoopCandidate>.Builder loops)
    {
        if (!cyclePlan.HasNaturalLoop(loop))
        {
            return;
        }

        var loopCycle = cyclePlan.FindLoop(loop);
        loops.Add(LoopCandidate.ForStructuredLoop(loop, body, cyclePlan, loopCycle));
    }

    internal static LabeledStatementSyntax? FindIncrementLabel(SyntaxList<StatementSyntax> statements, int labelIndex)
    {
        LabeledStatementSyntax? lastLabel = null;
        for (var i = labelIndex; i < statements.Count; i++)
        {
            for (var cur = statements[i]; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
            {
                lastLabel = labeled;
            }

            if (!JumpRegionPlanner.IsEmptyLabeledStatement(statements[i]))
            {
                break;
            }
        }

        return lastLabel;
    }

    private static ImmutableArray<LoopCandidate> ComputeHeldLoops(
        InvocationExpressionSyntax call,
        ImmutableArray<LoopCandidate> allLoops)
    {
        var containingStatement = GetContainingStatement(call);
        if (containingStatement == null)
        {
            return ImmutableArray<LoopCandidate>.Empty;
        }

        var held = ImmutableArray.CreateBuilder<LoopCandidate>();
        var left = ImmutableArray.CreateBuilder<LoopCandidate>();
        foreach (var loop in allLoops)
        {
            if (loop.Holds(call, containingStatement))
            {
                held.Add(loop);
            }
            else
            {
                left.Add(loop);
            }
        }

        bool changed;
        do
        {
            changed = false;
            for (var i = held.Count - 1; i >= 0; i--)
            {
                var candidate = held[i];
                var isNestedInLeft = false;
                var nestingTarget = (StatementSyntax?)candidate.SwitchOwner ?? candidate.HeaderStatement;
                foreach (var other in left)
                {
                    if ((candidate.SwitchOwner == null || other.SwitchOwner != candidate.SwitchOwner) &&
                        other.ContainsMemberStatement(nestingTarget))
                    {
                        isNestedInLeft = true;
                        break;
                    }
                }

                if (isNestedInLeft)
                {
                    held.RemoveAt(i);
                    left.Add(candidate);
                    changed = true;
                }
            }
        } while (changed);

        return held.ToImmutable();
    }

    private static StatementSyntax? GetContainingStatement(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (IsNestedCallable(current))
            {
                return null;
            }

            if (current is StatementSyntax statement)
            {
                return statement;
            }
        }

        return null;
    }

    internal static StatementSyntax OuterLabeledStatement(StatementSyntax statement)
    {
        var current = statement;
        while (current.Parent is LabeledStatementSyntax labeled)
        {
            current = labeled;
        }

        return current;
    }
}
