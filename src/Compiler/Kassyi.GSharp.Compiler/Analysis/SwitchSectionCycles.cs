using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

// The cycles that switch sections form with goto case and goto default, as loops of Warp.ActiveMask() keys
internal static class SwitchSectionCycles
{
    internal static void CollectSwitchSectionCycles(
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
        var jumps = ImmutableArray.CreateBuilder<(int From, GotoStatementSyntax Jump, int To, LabeledStatementSyntax? TargetLabel)>();

        for (var s = 0; s < sectionCount; s++)
        {
            foreach (var node in RemovedCalls.DescendantNodesThatRun(
                         sections[s],
                         semanticModel,
                         static (n, _) => ActiveMaskIterationPlanner.IsNestedCallable(n)))
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
                if (ActiveMaskIterationPlanner.FindIncrementLabel(section.Statements, headerIndex) is { } label)
                {
                    incrementLabelsBuilder.Add(label);
                }
            }
            else
            {
                if (incomingLabelIndices.Contains(0) &&
                    ActiveMaskIterationPlanner.FindIncrementLabel(section.Statements, 0) is { } headLabel)
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
                    if (idx > 0 && ActiveMaskIterationPlanner.FindIncrementLabel(section.Statements, idx) is { } midLabel &&
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

    internal static ImmutableHashSet<FlowNode> ComputeSwitchSectionMembers(
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
}
