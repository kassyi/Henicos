using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

// Where the CUDA iteration counters of Warp.ActiveMask() are reset: the goto edges of a statement list, the
// statements before a goto loop or after its label, and the place before a structured loop (past the Gpu hints
// that belong to it)
internal static class IterationCounterResets
{
    internal static void CollectListGotoEdges(
        SyntaxList<StatementSyntax> statements,
        int[] canonicalIndices,
        out ImmutableArray<(int K, int L)>.Builder forwardEdges,
        out ImmutableArray<(int K, int L)>.Builder backwardEdges)
    {
        forwardEdges = ImmutableArray.CreateBuilder<(int K, int L)>();
        backwardEdges = ImmutableArray.CreateBuilder<(int K, int L)>();
        for (var k = 0; k < statements.Count; k++)
        {
            foreach (var node in statements[k].DescendantNodesAndSelf(static n => !ActiveMaskIterationPlanner.IsNestedCallable(n)))
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

    internal static void ComputeGotoLoopResetTargets(
        SyntaxList<StatementSyntax> statements,
        int loopFirst,
        int loopLast,
        int[] canonicalIndices,
        ImmutableArray<(int K, int L)>.Builder forwardEdges,
        ImmutableArray<(int K, int L)>.Builder backwardEdges,
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

    internal static void AddResetPlacementForIndex(
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
            ActiveMaskIterationPlanner.FindIncrementLabel(statements, canonical) is { } postLabel)
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

    internal static void FindStructuredResetPlacement(
        StatementSyntax structuredLoop,
        SemanticModel semanticModel,
        HashSet<LabeledStatementSyntax> targetedLabels,
        out StatementSyntax? preReset,
        out LabeledStatementSyntax? postLabelReset)
    {
        preReset = null;
        postLabelReset = null;
        var outer = ActiveMaskIterationPlanner.OuterLabeledStatement(structuredLoop);
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

    internal static bool HasTargetedLabel(SyntaxNode? node, HashSet<LabeledStatementSyntax> targetedLabels)
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

    internal static StatementSyntax WalkBackGpuHints(
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

    internal static bool TryGetOwningList(StatementSyntax statement, out SyntaxList<StatementSyntax> statements)
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

    internal static bool IsGpuHintStatement(StatementSyntax statement, SemanticModel semanticModel) =>
        statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
        !RemovedCalls.IsRemoved(invocation, semanticModel) &&
        semanticModel.GetSymbol(invocation) is IMethodSymbol
        {
            ContainingType: { Name: TypeConstants.GpuMacros.Hint, ContainingType.Name: TypeConstants.GpuType } hintType
        } &&
        hintType.ContainingNamespace.IsCoreNamespace();
}
