namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Validates that <c>goto</c> statements in methods reaching <c>Warp.ActiveMask()</c> do not jump into the middle of a
/// reconvergence region (<c>ForwardRange</c>, <c>GotoLoop</c>, or a <c>switch</c> join cycle, GSH0120)
/// (<see cref="KernelBarrierValidator"/>).
/// </summary>
internal static class ReconvergenceJumpChecks
{
    /// <summary>Reports GSH0120 for every <c>goto</c> that enters the middle of a reconvergence region.</summary>
    internal static void Validate(KernelBarrierScan scan)
    {
        if (!scan.HasSplitPoint || ((scan.Gotos.Count == 0 || scan.Labels.Count == 0) && !scan.HasSwitchJump))
        {
            return;
        }

        var methodSymbol = scan.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(scan.Method);
        if (methodSymbol == null || !SuspensionAnalysis.ReachesActiveMask(methodSymbol, scan.SemanticModel.Compilation))
        {
            return;
        }

        foreach (var node in RemovedCalls.DescendantNodesThatRun(scan.Method, scan.SemanticModel))
        {
            switch (node)
            {
                case BlockSyntax block:
                    var plan = PlanStatements(scan.SemanticModel, block.Statements);
                    ReportRejectedGotos(scan, block.Statements, plan);
                    break;
                case SwitchStatementSyntax switchStatement:
                    ValidateSwitch(scan, switchStatement);
                    break;
            }
        }
    }

    private static JumpRegionPlan PlanStatements(SemanticModel model, SyntaxList<StatementSyntax> statements) =>
        JumpRegionPlanner.Plan(
            statements,
            statement => HoldsSplitPoint(statement, model));

    private static bool HoldsSplitPoint(StatementSyntax statement, SemanticModel model) =>
        RemovedCalls.DescendantNodesThatRun(statement, model)
            .AsStackEnumerable()
            .Any(model, static (node, m) =>
                node is InvocationExpressionSyntax inv &&
                m.GetSymbol(inv) is IMethodSymbol callee &&
                AstLoweringPipeline.ClassifySplitPoint(callee, m.Compilation) != null
            );

    private static void ValidateSwitch(KernelBarrierScan scan, SwitchStatementSyntax switchStatement)
    {
        var sectionPlans = new JumpRegionPlan[switchStatement.Sections.Count];
        for (var s = 0; s < switchStatement.Sections.Count; s++)
        {
            var statements = switchStatement.Sections[s].Statements;
            var plan = PlanStatements(scan.SemanticModel, statements);
            sectionPlans[s] = plan;
            ReportRejectedGotos(scan, statements, plan);
        }

        var crossSectionReported = ValidateCrossSectionGotos(scan, switchStatement, sectionPlans);
        ValidateSwitchCycles(scan, switchStatement, crossSectionReported);
    }

    private static HashSet<GotoStatementSyntax>? ValidateCrossSectionGotos(
        KernelBarrierScan scan,
        SwitchStatementSyntax switchStatement,
        JumpRegionPlan[] sectionPlans)
    {
        HashSet<GotoStatementSyntax>? reported = null;
        foreach (var rejected in JumpRegionPlanner.FindCrossSectionRejectedGotos(
                     switchStatement,
                     sectionPlans,
                     gotoStmt => !RemovedCalls.IsInRemovedCall(gotoStmt, scan.SemanticModel)))
        {
            reported ??= new HashSet<GotoStatementSyntax>();
            reported.Add(rejected.Goto);
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.GotoEntersReconvergenceRegionMiddle,
                rejected.Goto.GetLocation(),
                rejected.TargetLabelName,
                $"into the statements between '{rejected.RegionLabelName}:' and a goto to it that hold a warp " +
                "collective or barrier",
                $"Move the label '{rejected.TargetLabelName}' out of that range, or write the loop as a while or " +
                "for statement.");
        }

        return reported;
    }

    private static void ValidateSwitchCycles(
        KernelBarrierScan scan,
        SwitchStatementSyntax switchStatement,
        HashSet<GotoStatementSyntax>? crossSectionReported)
    {
        if (!SwitchStatementFlattener.CanFlatten(switchStatement) ||
            !switchStatement.Sections
                .AsStackEnumerable()
                .Any(scan.SemanticModel, static (section, model) =>
                    section.Statements
                        .AsStackEnumerable()
                        .Any(model, static (stmt, m) => HoldsSplitPoint(stmt, m))
                ))
        {
            return;
        }

        var masks = !scan.IsKernel ? new ArgumentMasks(ulong.MaxValue, 0) : default;
        if (scan.Flow.TryGetUniformity(masks, out var uniformity) &&
            !uniformity.IsThreadVariant(switchStatement.Expression))
        {
            return;
        }

        var sectionLookup = SwitchCaseLookup.CreateSectionLookup(switchStatement, scan.SemanticModel);
        var joinPlan = SwitchJoinPlanner.Plan(
            switchStatement,
            sectionLookup,
            gotoStmt => !RemovedCalls.IsInRemovedCall(gotoStmt, scan.SemanticModel));
        foreach (var rejected in joinPlan.Rejected)
        {
            if (crossSectionReported != null && crossSectionReported.Contains(rejected))
            {
                continue;
            }

            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.GotoEntersReconvergenceRegionMiddle,
                rejected.GetLocation(),
                FormatSwitchJumpTarget(rejected),
                "on a cycle between sections of a thread-dependent switch that holds a warp collective or barrier",
                "Break the cycle or write the loop as a while or for statement.");
        }
    }

    private static string FormatSwitchJumpTarget(GotoStatementSyntax gotoStmt) =>
        gotoStmt.Kind() switch
        {
            SyntaxKind.GotoDefaultStatement => "default",
            SyntaxKind.GotoCaseStatement => $"case {gotoStmt.Expression}",
            _ => gotoStmt.Expression is IdentifierNameSyntax targetId ? targetId.Identifier.ValueText : string.Empty
        };

    private static void ReportRejectedGotos(
        KernelBarrierScan scan,
        SyntaxList<StatementSyntax> statements,
        JumpRegionPlan plan)
    {
        foreach (var gotoStmt in plan.Rejected)
        {
            var targetLabelName = gotoStmt.Expression is IdentifierNameSyntax targetId
                ? targetId.Identifier.ValueText
                : string.Empty;
            var rangeLabelName = FindEnclosingRegionLabelName(statements, plan.Regions, gotoStmt, targetLabelName);
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.GotoEntersReconvergenceRegionMiddle,
                gotoStmt.GetLocation(),
                targetLabelName,
                $"into the statements between '{rangeLabelName}:' and a goto to it that hold a warp collective " +
                "or barrier",
                $"Move the label '{targetLabelName}' out of that range, or write the loop as a while or for " +
                "statement.");
        }
    }

    private static string FindEnclosingRegionLabelName(
        SyntaxList<StatementSyntax> statements,
        ImmutableArray<JumpRegion> regions,
        GotoStatementSyntax gotoStmt,
        string targetLabelName)
    {
        if (ReconvergenceSyntax.FindLabel(gotoStmt, targetLabelName) is { } targetLabel)
        {
            var l = JumpRegionPlanner.FindCanonicalStatementIndexForLabel(statements, targetLabel);
            var k = FindContainingStatementIndex(statements, gotoStmt);
            if (l >= 0 && k >= 0)
            {
                foreach (var range in regions)
                {
                    if (l > range.First && l <= range.Last && (k < range.First || k > range.Last))
                    {
                        return JumpRegionPlanner.FindRegionLabelName(statements, range);
                    }
                }
            }
        }

        return string.Empty;
    }

    private static int FindContainingStatementIndex(SyntaxList<StatementSyntax> statements, SyntaxNode node)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            if (ReconvergenceSyntax.IsInside(node, statements[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
