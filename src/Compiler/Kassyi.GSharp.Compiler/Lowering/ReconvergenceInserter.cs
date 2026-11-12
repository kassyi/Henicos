namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Makes the lanes of a warp that took different ways through a thread-dependent branch or loop wait for each other
/// where it ends, in a method that reaches <c>Warp.ActiveMask()</c> (#844).
/// </summary>
/// <remarks>
/// <para>
/// [WHY] The GPU runs the lanes of a warp that took different ways through a branch apart until the branch ends (its
/// immediate post-dominator), then together again, and <c>Warp.ActiveMask()</c> returns the lanes that run together. The
/// CPU paths run each lane up to its next split point in every launcher round, and <c>Warp.ActiveMask()</c> returns the
/// lanes that stopped at the same call in the same round. A lane that skips a branch would run on to the next call while
/// the lanes in the branch stop inside it, and meet them only by chance. So where such a branch or loop ends, each lane
/// stops (a reconvergence point, a split point) until no lane of its warp is still inside the same entry of it.
/// </para>
/// <para>
/// A region is entered before the branch or loop (<c>__gsharp_warp_region_enter</c>, which counts the entries of the
/// frame) and its end is the statement after it (<c>__gsharp_warp_reconverge_arrive</c>). A loop that some lanes
/// <c>continue</c> gets a second region around its body, which ends where the body ends, so that the lanes start each
/// iteration together. A jump out of a region (<c>break</c>, <c>continue</c>, <c>goto</c>, a <c>return</c> of a device
/// function) leaves it (<c>__gsharp_warp_region_leave</c>): the lanes that jump meet the others where the jump lands. A
/// <c>return</c> of a kernel needs nothing, as a lane that has exited takes part in nothing. A call to a device function
/// that suspends is a region as well: its lanes may leave it at different times (an early return, a sub-warp
/// shuffle inside it), and the GPU runs them together again where it returns.
/// </para>
/// <para>
/// Only branches and loops that hold a split point and whose condition may differ between the threads
/// (<see cref="UniformityAnalysis.IsThreadVariant"/>) get a region; any other branch keeps the lanes together by itself.
/// A condition the analysis cannot map back to the method counts as thread-dependent, which only costs a round. A
/// region whose end is the end of a kernel is left out: the lanes exit there.
/// </para>
/// <para>
/// Runs after <see cref="ConditionalEvaluationRewriter"/> and <see cref="WarpInstructionRewriter"/> have turned the
/// operands of <c>&amp;&amp;</c>, <c>||</c> and <c>?:</c> into statements, and before <see cref="BranchRewriter"/> and
/// <see cref="LoopRewriter"/> flatten the branches and loops: the regions follow the structure of the C# source.
/// </para>
/// </remarks>
internal sealed class ReconvergenceInserter : IBlockLoweringPass
{
    private readonly SemanticModel? _model;
    private readonly UniformityAnalysis? _uniformity;
    private readonly MethodDeclarationSyntax? _declaration;
    private readonly bool _isDeviceFunction;

    // The regions the statement being rewritten is inside, outermost first
    private readonly ImmutableArray<Region>.Builder _open = ImmutableArray.CreateBuilder<Region>();

    // The loops and switch statements the statement being rewritten is inside, outermost first
    private readonly ImmutableArray<JumpTarget>.Builder _targets = ImmutableArray.CreateBuilder<JumpTarget>();

    private ReconvergenceInserter(SemanticModel? model, UniformityAnalysis? uniformity,
        MethodDeclarationSyntax? declaration, bool isDeviceFunction)
    {
        _model = model;
        _uniformity = uniformity;
        _declaration = declaration;
        _isDeviceFunction = isDeviceFunction;
    }

    /// <summary>The regions numbered so far: the first warp sites of the frame (their entry counts).</summary>
    public int RegionCount { get; private set; }

    /// <summary>
    /// The pass for the body of <paramref name="owner"/>, or <see langword="null"/> when it does not reach
    /// <c>Warp.ActiveMask()</c> (<see cref="SuspensionAnalysis.ReachesActiveMask"/>).
    /// </summary>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="owner">The kernel or device function.</param>
    /// <param name="isDeviceFunction">
    /// Whether <paramref name="owner"/> is a device function: its parameters count as thread-dependent (a caller may pass
    /// each thread its own value), and its end returns to the caller instead of ending the thread.
    /// </param>
    public static ReconvergenceInserter? TryCreate(SemanticModel? model, IMethodSymbol? owner, bool isDeviceFunction)
    {
        if (model == null || owner == null || !SuspensionAnalysis.ReachesActiveMask(owner, model.Compilation))
        {
            return null;
        }

        UniformityAnalysis? uniformity = null;
        if (owner.TryGetDeclaringSyntax(out MethodDeclarationSyntax? declaration) &&
            model.Compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            var declarationModel = model.Compilation.GetCachedSemanticModel(declaration.SyntaxTree);
            var masks = isDeviceFunction ? new ArgumentMasks(ulong.MaxValue, 0) : default;
            if (!new MethodFlowGraph(declarationModel, declaration).TryGetUniformity(masks, out uniformity))
            {
                uniformity = null;
            }
        }

        return new ReconvergenceInserter(model, uniformity, declaration, isDeviceFunction);
    }

    /// <inheritdoc />
    public BlockSyntax Rewrite(BlockSyntax block) =>
        block.WithStatements(RewriteList(block.Statements, isKernelBody: !_isDeviceFunction));

    // isKernelBody: the statements of the kernel body, whose last statement ends the thread
    private SyntaxList<StatementSyntax> RewriteList(SyntaxList<StatementSyntax> statements, bool isKernelBody)
    {
        var rewritten = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count);
        for (var i = 0; i < statements.Count; i++)
        {
            var endsThread = isKernelBody && i == statements.Count - 1;
            AddRewritten(rewritten, statements[i], endsThread);
        }

        return SyntaxFactory.List(rewritten);
    }

    // The statement with its region around it, and the leaves of the regions a jump goes out of in front of it
    private void AddRewritten(ImmutableArray<StatementSyntax>.Builder statements, StatementSyntax statement,
        bool endsThread)
    {
        if (TryRewriteJump(statement, statements))
        {
            return;
        }

        var region = !endsThread && NeedsRegion(statement) ? OpenRegion(statement) : null;
        if (region == null)
        {
            statements.Add(RewriteStatement(statement, endsThread));
            return;
        }

        statements.Add(CreateRegionCall(GSharpInternalNames.Prelude.WarpRegionEnter, region.Site));
        var rewritten = RewriteStatement(statement, endsThread: false);
        _open.RemoveAt(_open.Count - 1);
        statements.Add(rewritten);
        statements.Add(CreateArrive(region.Site, GetPlaceNode(statement)));
    }

    // The node whose place names a region in the messages: the call of a call region (lowering may have moved it into a
    // statement of its own), the statement of a branch or loop
    private static SyntaxNode GetPlaceNode(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax or LocalDeclarationStatementSyntax
            ? statement.DescendantNodes()
                .AsStackEnumerable()
                .FirstOrDefault(static node => node is InvocationExpressionSyntax inv && BarrierHelper.IsSuspendingCall(inv))
              ?? statement
            : statement;

    // An embedded statement (the branch of an if, the body of a loop): one statement, a block when it grows
    private StatementSyntax RewriteEmbedded(StatementSyntax statement)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        AddRewritten(statements, statement, endsThread: false);
        return statements.Count == 1 ? statements[0] : SyntaxFactory.Block(statements);
    }

    // endsThread: the statement is the last of a kernel, and so is the last statement of a block it is (the loop that
    // Gpu.GridStrideLoop became, in its block)
    private StatementSyntax RewriteStatement(StatementSyntax statement, bool endsThread) =>
        statement switch
        {
            BlockSyntax block => block.WithStatements(RewriteList(block.Statements, isKernelBody: endsThread)),
            IfStatementSyntax ifStatement => RewriteIf(ifStatement),
            SwitchStatementSyntax switchStatement => RewriteSwitch(switchStatement),
            WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax =>
                RewriteLoop(statement),
            LabeledStatementSyntax labeled => labeled.WithStatement(RewriteEmbedded(labeled.Statement)),
            UnsafeStatementSyntax unsafeStatement => unsafeStatement.WithBlock(
                unsafeStatement.Block.WithStatements(RewriteList(unsafeStatement.Block.Statements, isKernelBody: false))
            ),
            CheckedStatementSyntax checkedStatement => checkedStatement.WithBlock(
                checkedStatement.Block.WithStatements(RewriteList(checkedStatement.Block.Statements, isKernelBody: false))
            ),
            _ => statement
        };

    private IfStatementSyntax RewriteIf(IfStatementSyntax ifStatement)
    {
        var rewritten = ifStatement.WithStatement(RewriteEmbedded(ifStatement.Statement));
        return ifStatement.Else is { } elseClause
            ? rewritten.WithElse(elseClause.WithStatement(RewriteEmbedded(elseClause.Statement)))
            : rewritten;
    }

    private SwitchStatementSyntax RewriteSwitch(SwitchStatementSyntax switchStatement)
    {
        _targets.Add(new JumpTarget(switchStatement, IsLoop: false, _open.Count, ContinueDepth: 0, ContinueLabel: null));
        var sections = ImmutableArray.CreateBuilder<SwitchSectionSyntax>(switchStatement.Sections.Count);
        foreach (var section in switchStatement.Sections)
        {
            sections.Add(section.WithStatements(RewriteList(section.Statements, isKernelBody: false)));
        }

        _targets.RemoveAt(_targets.Count - 1);
        return switchStatement.WithSections(SyntaxFactory.List(sections));
    }

    // A loop whose body some lanes continue gets a region around its body, which ends where the body ends; the continue
    // statements go there: { __gsharp_warp_region_enter(...); <body> __gsharp_ReconvergeContinue_<n>: ; __gsharp_warp_reconverge_arrive(...); }
    private StatementSyntax RewriteLoop(StatementSyntax loop)
    {
        var body = GetLoopBody(loop);
        var bodyRegion = BarrierHelper.ContainsPhaseBarrier(body) && HasContinue(loop) ? OpenRegion(body) : null;
        var continueLabel = bodyRegion != null
            ? $"{GSharpInternalNames.Lowering.ReconvergeContinueLabelPrefix}{bodyRegion.Site}"
            : null;
        var breakDepth = bodyRegion != null ? _open.Count - 1 : _open.Count;
        _targets.Add(new JumpTarget(loop, IsLoop: true, breakDepth, _open.Count, continueLabel));

        StatementSyntax rewrittenBody;
        if (bodyRegion != null && continueLabel != null)
        {
            var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
            statements.Add(CreateRegionCall(GSharpInternalNames.Prelude.WarpRegionEnter, bodyRegion.Site));
            var bodyStatements = body is BlockSyntax block ? block.Statements : SyntaxFactory.SingletonList(body);
            statements.AddRange(RewriteList(bodyStatements, isKernelBody: false));
            statements.Add(LoweringSyntax.EmptyLabel(continueLabel));
            statements.Add(CreateArrive(bodyRegion.Site, loop));
            _open.RemoveAt(_open.Count - 1);
            rewrittenBody = body is BlockSyntax bodyBlock
                ? bodyBlock.WithStatements(SyntaxFactory.List(statements))
                : SyntaxFactory.Block(statements);
        }
        else
        {
            rewrittenBody = RewriteEmbedded(body);
        }

        _targets.RemoveAt(_targets.Count - 1);
        return loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.WithStatement(rewrittenBody),
            DoStatementSyntax doStatement => doStatement.WithStatement(rewrittenBody),
            ForStatementSyntax forStatement => forStatement.WithStatement(rewrittenBody),
            CommonForEachStatementSyntax forEach => forEach.WithStatement(rewrittenBody),
            _ => throw new InvalidOperationException($"Not a loop: {loop.Kind()}")
        };
    }

    private static StatementSyntax GetLoopBody(StatementSyntax loop) =>
        loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            CommonForEachStatementSyntax forEach => forEach.Statement,
            _ => throw new InvalidOperationException($"Not a loop: {loop.Kind()}")
        };

    // A jump out of regions leaves them, innermost first; a continue of a loop with a body region goes to its end
    private bool TryRewriteJump(StatementSyntax statement, ImmutableArray<StatementSyntax>.Builder statements)
    {
        int leaveFrom;
        StatementSyntax jump = statement;
        switch (statement)
        {
            case BreakStatementSyntax when FindTarget(loopOnly: false) is { } target:
                leaveFrom = target.BreakDepth;
                break;
            case ContinueStatementSyntax when FindTarget(loopOnly: true) is { } target:
                leaveFrom = target.ContinueDepth;
                if (target.ContinueLabel != null)
                {
                    // goto __gsharp_ReconvergeContinue_<n>;
                    jump = LoweringSyntax.Goto(target.ContinueLabel).WithTriviaFrom(statement);
                }

                break;
            case ReturnStatementSyntax:
                // A lane that returns from a kernel has exited and takes part in nothing
                leaveFrom = _isDeviceFunction ? 0 : _open.Count;
                break;
            case GotoStatementSyntax gotoStatement when gotoStatement.IsKind(SyntaxKind.GotoStatement):
                leaveFrom = FindLabelDepth(gotoStatement);
                break;
            case GotoStatementSyntax when FindSwitch() is { } target:
                leaveFrom = target.BreakDepth;
                break;
            default:
                return false;
        }

        for (var i = _open.Count - 1; i >= leaveFrom; i--)
        {
            statements.Add(CreateRegionCall(GSharpInternalNames.Prelude.WarpRegionLeave, _open[i].Site));
        }

        statements.Add(jump);
        return true;
    }

    private JumpTarget? FindTarget(bool loopOnly)
    {
        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            if (!loopOnly || _targets[i].IsLoop)
            {
                return _targets[i];
            }
        }

        return null;
    }

    private JumpTarget? FindSwitch()
    {
        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            if (!_targets[i].IsLoop)
            {
                return _targets[i];
            }
        }

        return null;
    }

    // The regions that a goto stays inside: those around its label. C# lets a goto reach only a label of a block around it
    private int FindLabelDepth(GotoStatementSyntax gotoStatement)
    {
        if (gotoStatement.Expression is not IdentifierNameSyntax { Identifier.ValueText: var name } ||
            ReconvergenceSyntax.FindLabel(gotoStatement, name) is not { } label)
        {
            return 0;
        }

        var depth = 0;
        while (depth < _open.Count && ReconvergenceSyntax.IsInside(label, _open[depth].Extent))
        {
            depth++;
        }

        return depth;
    }

    private Region OpenRegion(SyntaxNode extent)
    {
        var region = new Region(RegionCount++, extent);
        _open.Add(region);
        return region;
    }

    // A branch or loop that holds a split point and whose lanes may take different ways through it, or a call to a device
    // function that suspends: its lanes may leave it at different times (an early return, a sub-warp shuffle), and the
    // GPU runs them together again where it returns
    private bool NeedsRegion(StatementSyntax statement) =>
        statement switch
        {
            ExpressionStatementSyntax or LocalDeclarationStatementSyntax => CallsSuspendingFunction(statement),
            IfStatementSyntax ifStatement =>
                (BarrierHelper.ContainsPhaseBarrier(ifStatement.Statement) ||
                 (ifStatement.Else is { } elseClause && BarrierHelper.ContainsPhaseBarrier(elseClause))) &&
                IsThreadVariant(ifStatement.Condition),
            SwitchStatementSyntax switchStatement =>
                switchStatement.Sections
                    .AsStackEnumerable()
                    .Any(BarrierHelper.ContainsPhaseBarrier) &&
                (IsThreadVariant(switchStatement.Expression) || HasWhenClause(switchStatement)),
            WhileStatementSyntax whileStatement =>
                BarrierHelper.ContainsPhaseBarrier(whileStatement) &&
                (IsThreadVariant(whileStatement.Condition) || HasExit(whileStatement)),
            DoStatementSyntax doStatement =>
                BarrierHelper.ContainsPhaseBarrier(doStatement) &&
                (IsThreadVariant(doStatement.Condition) || HasExit(doStatement)),
            ForStatementSyntax forStatement =>
                BarrierHelper.ContainsPhaseBarrier(forStatement.Statement) &&
                ((forStatement.Condition is { } condition && IsThreadVariant(condition)) || HasExit(forStatement)),
            CommonForEachStatementSyntax forEach =>
                BarrierHelper.ContainsPhaseBarrier(forEach.Statement) &&
                (IsThreadVariant(forEach.Expression) || HasExit(forEach)),
            _ => false
        };

    private static bool HasWhenClause(SwitchStatementSyntax switchStatement) =>
        switchStatement.Sections
            .AsStackEnumerable()
            .Any(static section => section.Labels
                .AsStackEnumerable()
                .Any(static label => label is CasePatternSwitchLabelSyntax { WhenClause: not null })
            );

    // A break of the loop, or a return or goto in its body, by which a lane may leave the loop before the others
    private static bool HasExit(StatementSyntax loop) =>
        GetLoopBody(loop)
            .DescendantNodesAndSelf(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .Any(loop, static (node, target) =>
                node is ReturnStatementSyntax ||
                node.IsKind(SyntaxKind.GotoStatement) ||
                (node is BreakStatementSyntax && ReconvergenceSyntax.FindJumpTarget(node, loopOnly: false) == target));

    // Whether a continue goes to the end of the body of loop
    private static bool HasContinue(StatementSyntax loop) =>
        GetLoopBody(loop)
            .DescendantNodesAndSelf(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .Any(loop, static (node, target) =>
                node is ContinueStatementSyntax && ReconvergenceSyntax.FindJumpTarget(node, loopOnly: true) == target);

    private static bool CallsSuspendingFunction(StatementSyntax statement) =>
        statement.DescendantNodes(static n => n is not AnonymousFunctionExpressionSyntax)
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && BarrierHelper.IsSuspendingCall(inv));

    // Whether the condition may differ between the threads of a block. One that the analysis cannot map back to a node of
    // the method (a copy of another method's code, a node lowering built) counts as thread-dependent
    private bool IsThreadVariant(ExpressionSyntax condition)
    {
        if (_uniformity == null || _model == null || _declaration == null ||
            _model.GetOriginalNode(condition) is not { } original ||
            original.SyntaxTree != _declaration.SyntaxTree ||
            !_declaration.Span.Contains(original.Span))
        {
            return true;
        }

        return _uniformity.IsThreadVariant(original);
    }

    // <function>(__gsharp_root, __gsharp_ctx, <site>);
    private static ExpressionStatementSyntax CreateRegionCall(string function, int site) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(function),
                SyntaxFactory.ArgumentList(CreateRegionArguments(site))
            )
        );

    // __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, <site>, "<place>"); a split point
    private ExpressionStatementSyntax CreateArrive(int site, SyntaxNode construct)
    {
        var place = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(SourcePlace.Format(_model, construct))
        );
        var arrive = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(GSharpInternalNames.Prelude.WarpReconvergeArrive),
            SyntaxFactory.ArgumentList(CreateRegionArguments(site).Add(SyntaxFactory.Argument(place)))
        ).WithAdditionalAnnotations(
            new SyntaxAnnotation(LoweringAnnotations.SyncIntrinsicAnnotationKind, LoweringAnnotations.ReconvergeAnnotationData)
        );
        return SyntaxFactory.ExpressionStatement(arrive);
    }

    // __gsharp_root, __gsharp_ctx, <site>
    private static SeparatedSyntaxList<ArgumentSyntax> CreateRegionArguments(int site) =>
        SyntaxFactory.SeparatedList<ArgumentSyntax>(
        [
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Root)),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context)),
            SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(site))
            )
        ]);

    // A region: its number (the warp site of its entry count) and the statement it covers
    private sealed record Region(int Site, SyntaxNode Extent);

    // A loop or switch that a break leaves (the regions from BreakDepth on are inside it) and, for a loop, where a
    // continue goes (the regions from ContinueDepth on are inside its body region)
    private sealed record JumpTarget(
        SyntaxNode Node,
        bool IsLoop,
        int BreakDepth,
        int ContinueDepth,
        string? ContinueLabel);
}
