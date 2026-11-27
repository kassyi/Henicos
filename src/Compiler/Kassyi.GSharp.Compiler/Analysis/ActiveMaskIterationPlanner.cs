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
                IsUniformLoop(loop, uniformity, semanticModel))
            {
                continue;
            }

            if (loop.StructuredLoop is ForStatementSyntax forLoop &&
                TryGetCountedLoopVariable(forLoop, body, semanticModel, uniformity, out var loopVariable))
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
                    FindStructuredResetPlacement(
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

    private static bool IsNestedCallable(SyntaxNode node) =>
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
        List<(int K, int L)>? forwardEdges = null;
        List<(int K, int L)>? backwardEdges = null;

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
            CollectListGotoEdges(statements, canonicalIndices, out forwardEdges, out backwardEdges);
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
                    ComputeGotoLoopResetTargets(
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

    private static void CollectListGotoEdges(
        SyntaxList<StatementSyntax> statements,
        int[] canonicalIndices,
        out List<(int K, int L)> forwardEdges,
        out List<(int K, int L)> backwardEdges)
    {
        forwardEdges = new List<(int K, int L)>();
        backwardEdges = new List<(int K, int L)>();
        for (var k = 0; k < statements.Count; k++)
        {
            foreach (var node in statements[k].DescendantNodesAndSelf(static n => !IsNestedCallable(n)))
            {
                if (node is not GotoStatementSyntax gotoStmt ||
                    !gotoStmt.IsKind(SyntaxKind.GotoStatement) ||
                    gotoStmt.Expression is not IdentifierNameSyntax { Identifier.ValueText: var labelName } ||
                    labelName.StartsWith(GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var targetLabel = ReconvergenceSyntax.FindLabel(gotoStmt, labelName);
                if (targetLabel == null)
                {
                    continue;
                }

                var l = JumpRegionPlanner.FindCanonicalStatementIndexForLabel(statements, targetLabel, canonicalIndices);
                if (l < 0)
                {
                    continue;
                }

                if (k < l)
                {
                    forwardEdges.Add((k, l));
                }
                else
                {
                    backwardEdges.Add((k, l));
                }
            }
        }
    }

    private static void ComputeGotoLoopResetTargets(
        SyntaxList<StatementSyntax> statements,
        int loopFirst,
        int loopLast,
        int[] canonicalIndices,
        List<(int K, int L)> forwardEdges,
        List<(int K, int L)> backwardEdges,
        SemanticModel semanticModel,
        out ImmutableArray<StatementSyntax> preResetStatements,
        out ImmutableArray<LabeledStatementSyntax> postLabelResetLabels)
    {
        var resetStart = loopFirst;
        bool changed;
        do
        {
            changed = false;
            foreach (var (k, l) in forwardEdges)
            {
                if (k < resetStart && l >= resetStart && l <= loopLast)
                {
                    resetStart = k;
                    changed = true;
                }
            }
        } while (changed);

        var enclosingHeaders = new HashSet<int>();
        foreach (var (kBack, lBack) in backwardEdges)
        {
            if (lBack < loopFirst && kBack >= loopFirst)
            {
                enclosingHeaders.Add(lBack);
            }
        }

        var preBuilder = ImmutableArray.CreateBuilder<StatementSyntax>();
        var postBuilder = ImmutableArray.CreateBuilder<LabeledStatementSyntax>();

        AddResetPlacementForIndex(
            statements,
            resetStart,
            canonicalIndices,
            enclosingHeaders,
            semanticModel,
            preBuilder,
            postBuilder);

        foreach (var lBack in enclosingHeaders)
        {
            if (lBack <= resetStart)
            {
                continue;
            }

            var innerStart = loopFirst;
            do
            {
                changed = false;
                foreach (var (k, l) in forwardEdges)
                {
                    if (k >= lBack && k < innerStart && l >= innerStart && l <= loopLast)
                    {
                        innerStart = k;
                        changed = true;
                    }
                }
            } while (changed);

            AddResetPlacementForIndex(
                statements,
                innerStart,
                canonicalIndices,
                enclosingHeaders,
                semanticModel,
                preBuilder,
                postBuilder);
        }

        preResetStatements = preBuilder.ToImmutable();
        postLabelResetLabels = postBuilder.ToImmutable();
    }

    private static void AddResetPlacementForIndex(
        SyntaxList<StatementSyntax> statements,
        int index,
        int[] canonicalIndices,
        HashSet<int> enclosingHeaders,
        SemanticModel semanticModel,
        ImmutableArray<StatementSyntax>.Builder preBuilder,
        ImmutableArray<LabeledStatementSyntax>.Builder postBuilder)
    {
        var canonical = canonicalIndices[index];
        if (enclosingHeaders.Contains(canonical) &&
            FindIncrementLabel(statements, canonical) is { } postLabel)
        {
            if (!postBuilder.Contains(postLabel))
            {
                postBuilder.Add(postLabel);
            }

            return;
        }

        var preStmt = WalkBackGpuHints(statements, index, semanticModel);
        if (!preBuilder.Contains(preStmt))
        {
            preBuilder.Add(preStmt);
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
                CollectSwitchSectionCycles(switchStatement, methodBody, semanticModel, ref switchFlow, loops);
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

    private static void CollectSwitchSectionCycles(
        SwitchStatementSyntax switchStatement,
        BlockSyntax methodBody,
        SemanticModel semanticModel,
        ref StatementFlow? switchFlow,
        ImmutableArray<LoopCandidate>.Builder loops)
    {
        var sections = switchStatement.Sections;
        var sectionCount = sections.Count;
        if (sectionCount == 0)
        {
            return;
        }

        var sectionLookup = SwitchCaseLookup.CreateSectionLookup(switchStatement, semanticModel);
        var reachable = new bool[sectionCount, sectionCount];
        var jumps = new List<(int From, GotoStatementSyntax Jump, int To, LabeledStatementSyntax? TargetLabel)>();

        for (var s = 0; s < sectionCount; s++)
        {
            foreach (var node in RemovedCalls.DescendantNodesThatRun(
                         sections[s],
                         semanticModel,
                         static (n, _) => IsNestedCallable(n)))
            {
                if (node is not GotoStatementSyntax gotoStmt)
                {
                    continue;
                }

                if (gotoStmt.IsKind(SyntaxKind.GotoCaseStatement, SyntaxKind.GotoDefaultStatement))
                {
                    if (ReconvergenceSyntax.EnclosingSwitchIs(gotoStmt, switchStatement))
                    {
                        var target = sectionLookup(gotoStmt);
                        if ((uint)target < (uint)sectionCount)
                        {
                            reachable[s, target] = true;
                            jumps.Add((s, gotoStmt, target, null));
                        }
                    }
                }
                else if (gotoStmt.IsKind(SyntaxKind.GotoStatement) &&
                         gotoStmt.Expression is IdentifierNameSyntax { Identifier.ValueText: var labelName } &&
                         !labelName.StartsWith(
                             GSharpInternalNames.Lowering.SyncGridNextLabelPrefix,
                             StringComparison.Ordinal) &&
                         ReconvergenceSyntax.FindLabel(gotoStmt, labelName) is { } targetLabel)
                {
                    for (var t = 0; t < sectionCount; t++)
                    {
                        if (t != s && JumpRegionPlanner.FindStatementIndexForLabel(sections[t].Statements, targetLabel) >= 0)
                        {
                            reachable[s, t] = true;
                            jumps.Add((s, gotoStmt, t, targetLabel));
                            break;
                        }
                    }
                }
            }
        }

        for (var k = 0; k < sectionCount; k++)
        {
            for (var i = 0; i < sectionCount; i++)
            {
                if (!reachable[i, k])
                {
                    continue;
                }

                for (var j = 0; j < sectionCount; j++)
                {
                    if (reachable[k, j])
                    {
                        reachable[i, j] = true;
                    }
                }
            }
        }

        var hasCycle = false;
        for (var s = 0; s < sectionCount; s++)
        {
            if (reachable[s, s])
            {
                hasCycle = true;
                break;
            }
        }

        if (!hasCycle)
        {
            return;
        }

        switchFlow ??= StatementFlow.Build(
            methodBody,
            condition => semanticModel.GetConstantCondition(condition),
            enterScopedStatements: true);

        for (var s = 0; s < sectionCount; s++)
        {
            if (!reachable[s, s])
            {
                continue;
            }

            var section = sections[s];
            if (section.Statements.Count == 0)
            {
                continue;
            }

            var sccIndices = new HashSet<int>();
            var sccSectionsBuilder = ImmutableArray.CreateBuilder<SwitchSectionSyntax>();
            for (var t = 0; t < sectionCount; t++)
            {
                if (reachable[s, t] && reachable[t, s])
                {
                    sccIndices.Add(t);
                    sccSectionsBuilder.Add(sections[t]);
                }
            }

            var backJumpsBuilder = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
            var hasCaseOrDefaultEntry = false;
            var incomingLabelIndices = new HashSet<int>();
            int[]? sectionCanonicalIndices = null;

            foreach (var jump in jumps)
            {
                if (jump.From == s && sccIndices.Contains(jump.To))
                {
                    backJumpsBuilder.Add(jump.Jump);
                }

                if (jump.To == s && sccIndices.Contains(jump.From))
                {
                    if (jump.TargetLabel == null)
                    {
                        hasCaseOrDefaultEntry = true;
                    }
                    else
                    {
                        sectionCanonicalIndices ??= JumpRegionPlanner.BuildCanonicalLabelIndices(section.Statements);
                        var idx = JumpRegionPlanner.FindCanonicalStatementIndexForLabel(
                            section.Statements,
                            jump.TargetLabel,
                            sectionCanonicalIndices);
                        if (idx >= 0)
                        {
                            incomingLabelIndices.Add(idx);
                        }
                    }
                }
            }

            if (backJumpsBuilder.Count == 0)
            {
                continue;
            }

            var headerIndex = 0;
            bool incrementSection;
            var incrementLabelsBuilder = ImmutableArray.CreateBuilder<LabeledStatementSyntax>();

            if (!hasCaseOrDefaultEntry && incomingLabelIndices.Count == 1)
            {
                foreach (var idx in incomingLabelIndices)
                {
                    headerIndex = idx;
                }

                incrementSection = false;
                if (FindIncrementLabel(section.Statements, headerIndex) is { } label)
                {
                    incrementLabelsBuilder.Add(label);
                }
            }
            else
            {
                if (incomingLabelIndices.Contains(0) &&
                    FindIncrementLabel(section.Statements, 0) is { } headLabel)
                {
                    incrementSection = false;
                    incrementLabelsBuilder.Add(headLabel);
                }
                else
                {
                    incrementSection = true;
                }

                foreach (var idx in incomingLabelIndices)
                {
                    if (idx > 0 && FindIncrementLabel(section.Statements, idx) is { } midLabel &&
                        !incrementLabelsBuilder.Contains(midLabel))
                    {
                        incrementLabelsBuilder.Add(midLabel);
                    }
                }
            }

            var backJumps = backJumpsBuilder.ToImmutable();
            var sectionEntry = StatementFlow.EntryOf(section.Statements[headerIndex]);
            var members = ComputeSwitchSectionMembers(switchFlow, sectionEntry, section, backJumps);

            loops.Add(LoopCandidate.ForSwitchSection(
                switchStatement,
                section,
                sccSectionsBuilder.ToImmutable(),
                section.Statements[headerIndex],
                headerIndex,
                incrementSection,
                incrementLabelsBuilder.ToImmutable(),
                members));
        }
    }

    private static ImmutableHashSet<FlowNode> ComputeSwitchSectionMembers(
        StatementFlow flow,
        FlowNode sectionEntry,
        SwitchSectionSyntax section,
        ImmutableArray<GotoStatementSyntax> backJumps)
    {
        var members = ImmutableHashSet.CreateBuilder<FlowNode>();
        var queue = new Queue<FlowNode>();
        members.Add(sectionEntry);

        foreach (var backJump in backJumps)
        {
            var jumpNode = flow.ResolveNode(backJump);
            if (jumpNode.Syntax is { } jumpSyntax &&
                ReconvergenceSyntax.IsInside(jumpSyntax, section) &&
                members.Add(jumpNode))
            {
                queue.Enqueue(jumpNode);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == sectionEntry)
            {
                continue;
            }

            foreach (var predecessor in flow.GetPredecessors(current))
            {
                if (predecessor.Syntax is { } predSyntax &&
                    ReconvergenceSyntax.IsInside(predSyntax, section) &&
                    members.Add(predecessor))
                {
                    queue.Enqueue(predecessor);
                }
            }
        }

        var reachable = ImmutableHashSet.CreateBuilder<FlowNode>();
        var forwardQueue = new Queue<FlowNode>();
        reachable.Add(sectionEntry);
        forwardQueue.Enqueue(sectionEntry);
        while (forwardQueue.Count > 0)
        {
            var current = forwardQueue.Dequeue();
            foreach (var successor in flow.GetSuccessors(current))
            {
                if (members.Contains(successor) && reachable.Add(successor))
                {
                    forwardQueue.Enqueue(successor);
                }
            }
        }

        return reachable.ToImmutable();
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

    private static LabeledStatementSyntax? FindIncrementLabel(SyntaxList<StatementSyntax> statements, int labelIndex)
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

    private static StatementSyntax OuterLabeledStatement(StatementSyntax statement)
    {
        var current = statement;
        while (current.Parent is LabeledStatementSyntax labeled)
        {
            current = labeled;
        }

        return current;
    }

    private static bool IsUniformLoop(
        LoopCandidate loop,
        UniformityAnalysis? uniformity,
        SemanticModel semanticModel)
    {
        if (uniformity == null)
        {
            return false;
        }

        if (loop.StructuredLoop is CommonForEachStatementSyntax topForEach &&
            uniformity.IsThreadVariant(topForEach.Expression))
        {
            return false;
        }

        foreach (var extentRoot in loop.GetExtentRoots())
        {
            if (extentRoot is StatementSyntax rootStatement &&
                ContainsNonUniformityInExtent(rootStatement, uniformity, semanticModel))
            {
                return false;
            }

            if (extentRoot is ExpressionSyntax rootExpression &&
                !RemovedCalls.IsRemovedCall(rootExpression, semanticModel))
            {
                if (((extentRoot.Parent is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax &&
                      extentRoot == GetLoopCondition(extentRoot.Parent)) ||
                     (extentRoot.Parent is SwitchStatementSyntax switchParent &&
                      extentRoot == switchParent.Expression)) &&
                    uniformity.IsThreadVariant(rootExpression))
                {
                    return false;
                }

                if (IsConditionOrSourceThreadVariant(rootExpression, uniformity) ||
                    IsMethodWithBodyCall(rootExpression, semanticModel) ||
                    ContainsNonUniformityInNode(rootExpression, uniformity, semanticModel))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ExpressionSyntax? GetLoopCondition(SyntaxNode? loop) => loop switch
    {
        ForStatementSyntax f => f.Condition,
        WhileStatementSyntax w => w.Condition,
        DoStatementSyntax d => d.Condition,
        _ => null
    };

    private static bool ContainsNonUniformityInExtent(
        StatementSyntax rootStatement,
        UniformityAnalysis uniformity,
        SemanticModel semanticModel)
    {
        if (IsConditionOrSourceThreadVariant(rootStatement, uniformity) ||
            IsMethodWithBodyCall(rootStatement, semanticModel))
        {
            return true;
        }

        return ContainsNonUniformityInNode(rootStatement, uniformity, semanticModel);
    }

    private static bool ContainsNonUniformityInNode(
        SyntaxNode root,
        UniformityAnalysis uniformity,
        SemanticModel semanticModel)
    {
        foreach (var node in RemovedCalls.DescendantNodesThatRun(
                     root,
                     semanticModel,
                     static (n, model) => IsNestedCallable(n) ||
                                          (n is InvocationExpressionSyntax inv && NameofOperator.Is(inv, model))))
        {
            if (IsConditionOrSourceThreadVariant(node, uniformity) ||
                IsMethodWithBodyCall(node, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConditionOrSourceThreadVariant(SyntaxNode node, UniformityAnalysis uniformity) =>
        node switch
        {
            IfStatementSyntax ifStatement => uniformity.IsThreadVariant(ifStatement.Condition),
            WhileStatementSyntax whileStatement => uniformity.IsThreadVariant(whileStatement.Condition),
            DoStatementSyntax doStatement => uniformity.IsThreadVariant(doStatement.Condition),
            ForStatementSyntax { Condition: { } condition } => uniformity.IsThreadVariant(condition),
            CommonForEachStatementSyntax forEach => uniformity.IsThreadVariant(forEach.Expression),
            SwitchStatementSyntax switchStatement => uniformity.IsThreadVariant(switchStatement.Expression),
            SwitchExpressionSyntax switchExpression => uniformity.IsThreadVariant(switchExpression.GoverningExpression),
            ConditionalExpressionSyntax conditional => uniformity.IsThreadVariant(conditional.Condition),
            ConditionalAccessExpressionSyntax conditionalAccess => uniformity.IsThreadVariant(conditionalAccess.Expression),
            BinaryExpressionSyntax binary when binary.IsKind(
                SyntaxKind.LogicalAndExpression,
                SyntaxKind.LogicalOrExpression,
                SyntaxKind.CoalesceExpression) => uniformity.IsThreadVariant(binary.Left),
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) =>
                uniformity.IsThreadVariant(assignment.Left),
            IsPatternExpressionSyntax isPattern => uniformity.IsThreadVariant(isPattern.Expression),
            CasePatternSwitchLabelSyntax { WhenClause: { } whenClause } =>
                uniformity.IsThreadVariant(whenClause.Condition),
            SwitchExpressionArmSyntax { WhenClause: { } whenClause } =>
                uniformity.IsThreadVariant(whenClause.Condition),
            _ => false
        };

    private static bool IsMethodWithBodyCall(SyntaxNode node, SemanticModel semanticModel)
    {
        var compilation = semanticModel.Compilation;
        if (node is InvocationExpressionSyntax invocation)
        {
            if (NameofOperator.Is(invocation, semanticModel))
            {
                return false;
            }

            if (semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                return true;
            }

            if (HasCallableBody(callee, compilation))
            {
                return true;
            }
        }

        if (node is ExpressionSyntax expression && expression is not ParenthesizedExpressionSyntax)
        {
            if (NameofOperator.Encloses(expression, semanticModel))
            {
                return false;
            }

            var conversion = semanticModel.GetSafeConversion(expression);
            if (conversion is { IsUserDefined: true, MethodSymbol: { } convMethod } &&
                HasCallableBody(convMethod, compilation))
            {
                return true;
            }
        }

        if (node is UsingStatementSyntax usingStatement)
        {
            var typeNode = (SyntaxNode?)usingStatement.Declaration?.Type ?? usingStatement.Expression;
            if (typeNode != null && UsingHasDisposableBody(typeNode, semanticModel))
            {
                return true;
            }
        }

        if (node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword } localUsing &&
            UsingHasDisposableBody(localUsing.Declaration.Type, semanticModel))
        {
            return true;
        }

        if (node is VariableDeclaratorSyntax
            {
                Parent.Parent: FixedStatementSyntax, Initializer.Value: { } fixedInit
            } &&
            PinnableReferenceLookup.Find(fixedInit, semanticModel) is { } pinnable &&
            HasCallableBody(pinnable.GetConstructedReducedFrom() ?? pinnable, compilation))
        {
            return true;
        }

        return semanticModel.GetSafeOperation(node) switch
        {
            IPropertyReferenceOperation { Property: var prop } =>
                (EmittedUserTypes.IsEmittedUserType(prop.ContainingType) &&
                 StructAbiMembers.HasAccessorFunctions(prop)) ||
                (prop.GetMethod is { } getter && HasCallableBody(getter, compilation)) ||
                (prop.SetMethod is { } setter && HasCallableBody(setter, compilation)),
            IObjectCreationOperation { Constructor: { IsImplicitlyDeclared: false } ctor } =>
                HasCallableBody(ctor, compilation),
            IBinaryOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            IUnaryOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            IIncrementOrDecrementOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            ICompoundAssignmentOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            IConversionOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            _ => false
        };
    }

    private static bool UsingHasDisposableBody(SyntaxNode typeOrExpression, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeSymbol(typeOrExpression);
        return type is INamedTypeSymbol named &&
               EmittedUserTypes.IsEmittedUserType(named) &&
               UsingDisposeLookup.Find(named, semanticModel.Compilation) is { } dispose &&
               HasCallableBody(dispose, semanticModel.Compilation);
    }

    private static bool HasCallableBody(IMethodSymbol callee, Compilation compilation)
    {
        if (callee.MethodKind is MethodKind.LocalFunction or MethodKind.DelegateInvoke or
            MethodKind.AnonymousFunction)
        {
            return true;
        }

        if (GpuMacroClassifier.Classify(callee).HasBody)
        {
            return true;
        }

        if (callee.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
            DeviceExportTable.TryFind(callee, out _))
        {
            return true;
        }

        return DeviceFunctionBodies.TryGet(callee, compilation, out var bodies) && bodies.Length > 0;
    }

    private static bool TryGetCountedLoopVariable(
        ForStatementSyntax forLoop,
        BlockSyntax methodBody,
        SemanticModel semanticModel,
        UniformityAnalysis? uniformity,
        [NotNullWhen(true)] out ILocalSymbol? loopVariable)
    {
        loopVariable = null;
        if (uniformity == null ||
            forLoop.SyntaxTree != semanticModel.SyntaxTree ||
            forLoop.Declaration is not { Variables: [{ Initializer.Value: { } initializerValue } declarator] } ||
            forLoop.Incrementors.Count != 1 ||
            forLoop.Condition is not { } condition)
        {
            return false;
        }

        var local = semanticModel.GetDeclaredSymbol<ILocalSymbol>(declarator);
        if (local is not { RefKind: RefKind.None } ||
            local.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32))
        {
            return false;
        }

        if (uniformity.IsThreadVariant(initializerValue))
        {
            return false;
        }

        if (!TryGetUnitStepDirection(forLoop.Incrementors[0], local, semanticModel, out var isIncrement))
        {
            return false;
        }

        if (!IsStrictBoundingComparison(condition, local, isIncrement, semanticModel))
        {
            return false;
        }

        if (WritesLocal(semanticModel.AnalyzeDataFlow(forLoop.Statement), local) ||
            WritesLocal(semanticModel.AnalyzeDataFlow(condition), local))
        {
            return false;
        }

        var hasAliasOrCapturedWrite = methodBody.DescendantNodes()
            .AsStackEnumerable()
            .Any((Model: semanticModel, Local: local), static (node, state) =>
                TakesReference(node, state.Model, state.Local) ||
                WritesCaptured(node, state.Model, state.Local));
        if (hasAliasOrCapturedWrite)
        {
            return false;
        }

        loopVariable = local;
        return true;
    }

    private static bool TryGetUnitStepDirection(
        ExpressionSyntax incrementor,
        ILocalSymbol loopVariable,
        SemanticModel semanticModel,
        out bool isIncrement)
    {
        isIncrement = false;
        var operation = semanticModel.GetOperation(incrementor);
        switch (operation)
        {
            case IIncrementOrDecrementOperation { OperatorMethod: null, IsLifted: false } incOp
                when incOp.Target is ILocalReferenceOperation target &&
                     SymbolEqualityComparer.Default.Equals(target.Local, loopVariable):
                isIncrement = incOp.Kind == OperationKind.Increment;
                return incOp.Kind is OperationKind.Increment or OperationKind.Decrement;

            case ICompoundAssignmentOperation { OperatorMethod: null, IsLifted: false } compOp
                when compOp.Target is ILocalReferenceOperation target &&
                     SymbolEqualityComparer.Default.Equals(target.Local, loopVariable) &&
                     compOp.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract &&
                     compOp.Value.ConstantValue is { HasValue: true, Value: 1 or 1u }:
                isIncrement = compOp.OperatorKind == BinaryOperatorKind.Add;
                return true;

            default:
                return false;
        }
    }

    private static bool IsStrictBoundingComparison(
        ExpressionSyntax condition,
        ILocalSymbol loopVariable,
        bool isIncrement,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(condition) is not IBinaryOperation
            {
                OperatorMethod: null,
                IsLifted: false
            } comparison)
        {
            return false;
        }

        BinaryOperatorKind kind;
        if (comparison.LeftOperand is ILocalReferenceOperation leftLocal &&
            SymbolEqualityComparer.Default.Equals(leftLocal.Local, loopVariable) &&
            SymbolEqualityComparer.Default.Equals(comparison.RightOperand.Type, loopVariable.Type))
        {
            kind = comparison.OperatorKind;
        }
        else if (comparison.RightOperand is ILocalReferenceOperation rightLocal &&
                 SymbolEqualityComparer.Default.Equals(rightLocal.Local, loopVariable) &&
                 SymbolEqualityComparer.Default.Equals(comparison.LeftOperand.Type, loopVariable.Type))
        {
            kind = FlipComparison(comparison.OperatorKind);
        }
        else
        {
            return false;
        }

        return (kind, isIncrement) switch
        {
            (BinaryOperatorKind.LessThan, true) => true,
            (BinaryOperatorKind.GreaterThan, false) => true,
            _ => false
        };
    }

    private static BinaryOperatorKind FlipComparison(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
        _ => kind
    };

    private static bool WritesLocal(DataFlowAnalysis? flow, ILocalSymbol local) =>
        flow is not { Succeeded: true } ||
        flow.WrittenInside.Contains(local) ||
        flow.UnsafeAddressTaken.Contains(local);

    private static bool WritesCaptured(SyntaxNode node, SemanticModel model, ILocalSymbol local)
    {
        var body = node switch
        {
            AnonymousFunctionExpressionSyntax lambda => lambda.Body,
            LocalFunctionStatementSyntax { Body: { } block } => block,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
            _ => null
        };
        return body != null && WritesLocal(model.AnalyzeDataFlow(body), local);
    }

    private static bool TakesReference(SyntaxNode node, SemanticModel model, ILocalSymbol local)
    {
        var expression = node switch
        {
            ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } arg =>
                arg.Expression,
            RefExpressionSyntax reference => reference.Expression,
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.AddressOfExpression } address => address.Operand,
            _ => null
        };
        return expression != null &&
               expression.UnwrapParentheses() is IdentifierNameSyntax identifier &&
               SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, local);
    }

    private static void FindStructuredResetPlacement(
        StatementSyntax structuredLoop,
        SemanticModel semanticModel,
        HashSet<LabeledStatementSyntax> targetedLabels,
        out StatementSyntax? preReset,
        out LabeledStatementSyntax? postLabelReset)
    {
        preReset = null;
        postLabelReset = null;
        var outer = OuterLabeledStatement(structuredLoop);
        if (TryGetOwningList(outer, out var statements))
        {
            var index = statements.IndexOf(outer);
            if (index >= 0)
            {
                var walked = WalkBackGpuHints(statements, index, semanticModel);
                if (walked == outer &&
                    structuredLoop.Parent is LabeledStatementSyntax innermostLabel &&
                    HasTargetedLabel(structuredLoop.Parent, targetedLabels))
                {
                    postLabelReset = innermostLabel;
                    return;
                }

                preReset = walked;
                return;
            }
        }

        preReset = outer;
    }

    private static bool HasTargetedLabel(SyntaxNode? node, HashSet<LabeledStatementSyntax> targetedLabels)
    {
        for (var cur = node; cur is LabeledStatementSyntax labeled; cur = cur.Parent)
        {
            if (targetedLabels.Contains(labeled))
            {
                return true;
            }
        }

        return false;
    }

    private static StatementSyntax WalkBackGpuHints(
        SyntaxList<StatementSyntax> statements,
        int index,
        SemanticModel semanticModel)
    {
        var resetIndex = index;
        while (resetIndex > 0 && IsGpuHintStatement(statements[resetIndex - 1], semanticModel))
        {
            resetIndex--;
        }

        return statements[resetIndex];
    }

    private static bool TryGetOwningList(StatementSyntax statement, out SyntaxList<StatementSyntax> statements)
    {
        switch (statement.Parent)
        {
            case BlockSyntax block:
                statements = block.Statements;
                return true;
            case SwitchSectionSyntax section:
                statements = section.Statements;
                return true;
            default:
                statements = default;
                return false;
        }
    }

    private static bool IsGpuHintStatement(StatementSyntax statement, SemanticModel semanticModel) =>
        statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
        !RemovedCalls.IsRemoved(invocation, semanticModel) &&
        semanticModel.GetSymbol(invocation) is IMethodSymbol
        {
            ContainingType: { Name: TypeConstants.GpuMacros.Hint, ContainingType.Name: TypeConstants.GpuType } hintType
        } &&
        hintType.ContainingNamespace.IsCoreNamespace();

    private sealed class LoopCandidate
    {
        private LoopCandidate(
            StatementSyntax headerStatement,
            StatementSyntax? structuredLoop,
            StatementSyntax? structuredBody,
            CyclePlan? cyclePlan,
            LoopCycle? loopCycle,
            SyntaxList<StatementSyntax> gotoStatements,
            SyntaxNode? gotoOwner,
            int gotoFirst,
            int gotoLast,
            Cycle? gotoCycle,
            LabeledStatementSyntax? incrementLabel,
            ImmutableArray<StatementSyntax> preResetStatements,
            ImmutableArray<LabeledStatementSyntax> postLabelResetLabels,
            SwitchStatementSyntax? switchOwner,
            SwitchSectionSyntax? switchSection,
            ImmutableArray<SwitchSectionSyntax> switchSccSections,
            int switchHeaderIndex,
            bool incrementSwitchSection,
            ImmutableArray<LabeledStatementSyntax> switchIncrementLabels,
            ImmutableHashSet<FlowNode>? switchSectionMembers)
        {
            HeaderStatement = headerStatement;
            StructuredLoop = structuredLoop;
            StructuredBody = structuredBody;
            CyclePlan = cyclePlan;
            LoopCycle = loopCycle;
            GotoStatements = gotoStatements;
            GotoOwner = gotoOwner;
            GotoFirst = gotoFirst;
            GotoLast = gotoLast;
            GotoCycle = gotoCycle;
            IncrementLabel = incrementLabel;
            PreResetStatements = preResetStatements;
            PostLabelResetLabels = postLabelResetLabels;
            SwitchOwner = switchOwner;
            SwitchSection = switchSection;
            SwitchSccSections = switchSccSections;
            SwitchHeaderIndex = switchHeaderIndex;
            IncrementSwitchSection = incrementSwitchSection;
            SwitchIncrementLabels = switchIncrementLabels;
            SwitchSectionMembers = switchSectionMembers;
        }

        public StatementSyntax HeaderStatement { get; }
        public StatementSyntax? StructuredLoop { get; }
        public StatementSyntax? StructuredBody { get; }
        public CyclePlan? CyclePlan { get; }
        public LoopCycle? LoopCycle { get; }
        public SyntaxList<StatementSyntax> GotoStatements { get; }
        public SyntaxNode? GotoOwner { get; }
        public int GotoFirst { get; }
        public int GotoLast { get; }
        public Cycle? GotoCycle { get; }
        public LabeledStatementSyntax? IncrementLabel { get; }
        public ImmutableArray<StatementSyntax> PreResetStatements { get; }
        public ImmutableArray<LabeledStatementSyntax> PostLabelResetLabels { get; }
        public SwitchStatementSyntax? SwitchOwner { get; }
        public SwitchSectionSyntax? SwitchSection { get; }
        public ImmutableArray<SwitchSectionSyntax> SwitchSccSections { get; }
        public int SwitchHeaderIndex { get; }
        public bool IncrementSwitchSection { get; }
        public ImmutableArray<LabeledStatementSyntax> SwitchIncrementLabels { get; }
        public ImmutableHashSet<FlowNode>? SwitchSectionMembers { get; }

        public static LoopCandidate ForStructuredLoop(
            StatementSyntax loop,
            StatementSyntax body,
            CyclePlan cyclePlan,
            LoopCycle? loopCycle) =>
            new(
                OuterLabeledStatement(loop),
                loop,
                body,
                cyclePlan,
                loopCycle,
                default,
                null,
                -1,
                -1,
                null,
                null,
                ImmutableArray<StatementSyntax>.Empty,
                ImmutableArray<LabeledStatementSyntax>.Empty,
                null,
                null,
                ImmutableArray<SwitchSectionSyntax>.Empty,
                0,
                false,
                ImmutableArray<LabeledStatementSyntax>.Empty,
                null);

        public static LoopCandidate ForGotoLoop(
            StatementSyntax header,
            SyntaxList<StatementSyntax> statements,
            SyntaxNode owner,
            int first,
            int last,
            CyclePlan cyclePlan,
            Cycle? cycle,
            LabeledStatementSyntax? incrementLabel,
            ImmutableArray<StatementSyntax> preResetStatements,
            ImmutableArray<LabeledStatementSyntax> postLabelResetLabels) =>
            new(
                header,
                null,
                null,
                cyclePlan,
                null,
                statements,
                owner,
                first,
                last,
                cycle,
                incrementLabel,
                preResetStatements,
                postLabelResetLabels,
                null,
                null,
                ImmutableArray<SwitchSectionSyntax>.Empty,
                0,
                false,
                ImmutableArray<LabeledStatementSyntax>.Empty,
                null);

        public static LoopCandidate ForSwitchSection(
            SwitchStatementSyntax switchOwner,
            SwitchSectionSyntax switchSection,
            ImmutableArray<SwitchSectionSyntax> sccSections,
            StatementSyntax headerStatement,
            int headerIndex,
            bool incrementSwitchSection,
            ImmutableArray<LabeledStatementSyntax> switchIncrementLabels,
            ImmutableHashSet<FlowNode> members) =>
            new(
                headerStatement,
                null,
                null,
                null,
                null,
                default,
                null,
                -1,
                -1,
                null,
                null,
                ImmutableArray<StatementSyntax>.Empty,
                ImmutableArray<LabeledStatementSyntax>.Empty,
                switchOwner,
                switchSection,
                sccSections,
                headerIndex,
                incrementSwitchSection,
                switchIncrementLabels,
                members);

        public IEnumerable<SyntaxNode> GetExtentRoots()
        {
            if (SwitchSection != null && SwitchOwner != null)
            {
                if (SwitchSccSections.Length > 1)
                {
                    yield return SwitchOwner.Expression;
                }

                foreach (var sccSection in SwitchSccSections)
                {
                    foreach (var label in sccSection.Labels)
                    {
                        if (label is CasePatternSwitchLabelSyntax { WhenClause: { } whenClause })
                        {
                            yield return whenClause.Condition;
                        }
                    }

                    foreach (var stmt in sccSection.Statements)
                    {
                        yield return stmt;
                    }
                }

                yield break;
            }

            if (StructuredLoop != null && StructuredBody != null)
            {
                yield return StructuredBody;
                switch (StructuredLoop)
                {
                    case ForStatementSyntax forStatement:
                        if (forStatement.Condition != null)
                        {
                            yield return forStatement.Condition;
                        }

                        foreach (var incrementor in forStatement.Incrementors)
                        {
                            yield return incrementor;
                        }

                        break;
                    case WhileStatementSyntax whileStatement:
                        yield return whileStatement.Condition;
                        break;
                    case DoStatementSyntax doStatement:
                        yield return doStatement.Condition;
                        break;
                }

                yield break;
            }

            for (var i = GotoFirst; i <= GotoLast; i++)
            {
                yield return GotoStatements[i];
            }
        }

        public bool ContainsMemberStatement(StatementSyntax statement)
        {
            var outer = OuterLabeledStatement(statement);
            if (SwitchSection != null)
            {
                return SwitchSectionMembers != null &&
                       SwitchSectionMembers.Contains(StatementFlow.EntryOf(outer));
            }

            if (StructuredLoop != null)
            {
                if (LoopCycle != null)
                {
                    return LoopCycle.Members.Contains(StatementFlow.EntryOf(outer));
                }

                return IsInStructuredExtent(outer);
            }

            if (GotoCycle != null)
            {
                return GotoCycle.Members.Contains(StatementFlow.EntryOf(outer));
            }

            return IsInGotoExtent(outer);
        }

        public bool Holds(InvocationExpressionSyntax call, StatementSyntax containingStatement)
        {
            var outerStatement = OuterLabeledStatement(containingStatement);
            if (SwitchSection != null)
            {
                return IsInSwitchSectionExtent(outerStatement);
            }

            if (StructuredLoop != null)
            {
                if (StructuredLoop == containingStatement)
                {
                    return IsInStructuredHeaderExtent(call, StructuredLoop);
                }

                if (!IsInStructuredExtent(outerStatement))
                {
                    return false;
                }

                if (LoopCycle == null)
                {
                    return true;
                }

                if (LoopCycle.Members.Contains(StatementFlow.EntryOf(outerStatement)))
                {
                    return true;
                }

                return !IsPastStructuredGate(outerStatement);
            }

            if (!IsInGotoExtent(outerStatement))
            {
                return false;
            }

            if (GotoCycle == null)
            {
                return true;
            }

            if (GotoCycle.Members.Contains(StatementFlow.EntryOf(outerStatement)))
            {
                return true;
            }

            return !IsPastGotoGate(outerStatement);
        }

        private bool IsInSwitchSectionExtent(SyntaxNode node)
        {
            if (SwitchSection == null)
            {
                return false;
            }

            for (var current = node; current != null; current = current.Parent)
            {
                if (IsNestedCallable(current))
                {
                    return false;
                }

                if (current is StatementSyntax statement && statement.Parent == SwitchSection)
                {
                    var index = SwitchSection.Statements.IndexOf(statement);
                    return index >= SwitchHeaderIndex;
                }
            }

            return false;
        }

        private static bool IsInStructuredHeaderExtent(InvocationExpressionSyntax call, StatementSyntax loop) =>
            loop switch
            {
                ForStatementSyntax forStatement =>
                    (forStatement.Condition != null && forStatement.Condition.Span.Contains(call.Span)) ||
                    forStatement.Incrementors
                        .AsStackEnumerable()
                        .Any(call.Span, static (inc, span) => inc.Span.Contains(span)),
                WhileStatementSyntax whileStatement => whileStatement.Condition.Span.Contains(call.Span),
                DoStatementSyntax doStatement => doStatement.Condition.Span.Contains(call.Span),
                _ => false
            };

        private bool IsInStructuredExtent(SyntaxNode node)
        {
            if (StructuredBody == null)
            {
                return false;
            }

            for (var current = node; current != null; current = current.Parent)
            {
                if (IsNestedCallable(current))
                {
                    return false;
                }

                if (current == StructuredBody)
                {
                    return true;
                }

                if (current == StructuredLoop)
                {
                    return false;
                }
            }

            return false;
        }

        private bool IsInGotoExtent(SyntaxNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (IsNestedCallable(current))
                {
                    return false;
                }

                if (current is StatementSyntax statement &&
                    statement.Parent == GotoOwner)
                {
                    var index = GotoStatements.IndexOf(statement);
                    return index >= GotoFirst && index <= GotoLast;
                }
            }

            return false;
        }

        private bool IsPastStructuredGate(StatementSyntax outerStatement)
        {
            if (CyclePlan == null || LoopCycle == null || StructuredBody == null)
            {
                return false;
            }

            SyntaxNode? current = outerStatement;
            while (current != null)
            {
                if (current is StatementSyntax stmt)
                {
                    var stmtOuter = OuterLabeledStatement(stmt);
                    if (CyclePlan.IsGateStatement(LoopCycle, stmtOuter))
                    {
                        return true;
                    }

                    if (TryGetOwningList(stmtOuter, out var list))
                    {
                        var index = list.IndexOf(stmtOuter);
                        for (var k = 0; k <= index; k++)
                        {
                            if (CyclePlan.IsGateStatement(LoopCycle, list[k]))
                            {
                                return true;
                            }
                        }
                    }

                    if (stmtOuter.Parent == StructuredBody || stmtOuter == OuterLabeledStatement(StructuredBody))
                    {
                        return false;
                    }
                }

                if (current == StructuredBody || current == StructuredLoop)
                {
                    return false;
                }

                current = current.Parent;
            }

            return false;
        }

        private bool IsPastGotoGate(StatementSyntax outerStatement)
        {
            if (CyclePlan == null || GotoCycle == null)
            {
                return false;
            }

            SyntaxNode? current = outerStatement;
            while (current != null)
            {
                if (current is StatementSyntax stmt)
                {
                    var stmtOuter = OuterLabeledStatement(stmt);
                    if (CyclePlan.IsGateStatement(GotoCycle, stmtOuter))
                    {
                        return true;
                    }

                    if (TryGetOwningList(stmtOuter, out var list))
                    {
                        var index = list.IndexOf(stmtOuter);
                        for (var k = 0; k <= index; k++)
                        {
                            if (CyclePlan.IsGateStatement(GotoCycle, list[k]))
                            {
                                return true;
                            }
                        }
                    }

                    if (stmtOuter.Parent == GotoOwner)
                    {
                        return false;
                    }
                }

                if (current == GotoOwner)
                {
                    return false;
                }

                current = current.Parent;
            }

            return false;
        }
    }
}
