using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Validates that <c>goto</c> statements in methods reaching <c>Warp.ActiveMask()</c> do not enter the middle of a
/// reconvergence range (<see cref="JumpRegionPlanner"/>, GSH0120).
/// </summary>
internal static class ReconvergenceJumpChecks
{
    internal static void Validate(KernelBarrierScan scan)
    {
        if (scan.Gotos.Count == 0 || scan.Labels.Count == 0 || !scan.HasSplitPoint)
        {
            return;
        }

        var methodSymbol = scan.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(scan.Method);
        if (methodSymbol == null ||
            !SuspensionAnalysis.ReachesActiveMask(methodSymbol, scan.SemanticModel.Compilation))
        {
            return;
        }

        var model = scan.SemanticModel;
        var compilation = model.Compilation;

        bool HoldsSplitPoint(StatementSyntax statement)
        {
            foreach (var node in RemovedCalls.DescendantNodesThatRun(statement, model))
            {
                if (node is InvocationExpressionSyntax inv &&
                    model.GetSymbol(inv) is IMethodSymbol callee &&
                    AstLoweringPipeline.ClassifySplitPoint(callee, compilation) != null)
                {
                    return true;
                }
            }

            return false;
        }

        bool IsRemovedGoto(GotoStatementSyntax gotoStmt) => RemovedCalls.IsInRemovedCall(gotoStmt, model);

        foreach (var node in DescendantNodesInLoweredBody(scan.Method, model))
        {
            switch (node)
            {
                case BlockSyntax block:
                {
                    var plan = JumpRegionPlanner.Plan(
                        block.Statements,
                        HoldsSplitPoint,
                        splitStackedLabels: true,
                        IsRemovedGoto);
                    ReportRejected(scan, plan.RejectedDetails);
                    break;
                }
                case SwitchStatementSyntax switchStatement:
                {
                    var sectionPlans = new JumpRegionPlan[switchStatement.Sections.Count];
                    for (var s = 0; s < switchStatement.Sections.Count; s++)
                    {
                        var sectionPlan = JumpRegionPlanner.Plan(
                            switchStatement.Sections[s].Statements,
                            HoldsSplitPoint,
                            splitStackedLabels: true,
                            IsRemovedGoto);
                        sectionPlans[s] = sectionPlan;
                        ReportRejected(scan, sectionPlan.RejectedDetails);
                    }

                    var crossSection = JumpRegionPlanner.FindRejectedCrossSectionGotos(
                        switchStatement,
                        sectionPlans,
                        splitStackedLabels: true,
                        HoldsSplitPoint,
                        IsRemovedGoto);
                    ReportRejected(scan, crossSection);
                    break;
                }
            }
        }
    }

    private static IEnumerable<SyntaxNode> DescendantNodesInLoweredBody(SyntaxNode root, SemanticModel model)
    {
        var pending = new Stack<SyntaxNode>();
        PushChildren(pending, root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (RemovedCalls.IsRemovedCall(node as ExpressionSyntax, model) ||
                node is LocalFunctionStatementSyntax ||
                (node is AnonymousFunctionExpressionSyntax &&
                 !(node is LambdaExpressionSyntax lambda && IsExpandedMacroBody(lambda, model))))
            {
                continue;
            }

            yield return node;
            PushChildren(pending, node);
        }
    }

    private static bool IsExpandedMacroBody(LambdaExpressionSyntax lambda, SemanticModel model) =>
        GpuMacroClassifier.ClassifyBody(lambda, model).Kind is
            GpuMacroKind.ForUnroll or GpuMacroKind.GridStrideLoop or GpuMacroKind.SyncGrid;

    private static void PushChildren(Stack<SyntaxNode> pending, SyntaxNode node)
    {
        foreach (var child in node.ChildNodesAndTokens().Reverse())
        {
            if (child.AsNode() is { } childNode)
            {
                pending.Push(childNode);
            }
        }
    }

    private static void ReportRejected(KernelBarrierScan scan, ImmutableArray<RejectedJump> rejected)
    {
        if (rejected.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var item in rejected)
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.ReconvergenceJumpProhibited,
                item.Statement.GetLocation(),
                item.TargetLabelName,
                item.RegionLabelName);
        }
    }
}
