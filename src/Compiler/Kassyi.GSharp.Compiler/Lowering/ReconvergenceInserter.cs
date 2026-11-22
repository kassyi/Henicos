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
/// A region is entered before the branch or loop (<c>__gsharp_warp_region_enter</c>) and its end is the statement
/// after it (<c>__gsharp_warp_reconverge_arrive</c>). A loop that some lanes
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
/// switch that holds a split point also gets a region when any section has a nested exit, even if its expression
/// is uniform, because lanes that jump out early would otherwise run ahead of the lanes that stay inside. A
/// thread-dependent switch that holds a split point also places join regions on section entries or labels reached from
/// another section, so lanes jumping there meet the lanes arriving directly. A region whose end is the end of a kernel
/// is left out: the lanes exit there.
/// </para>
/// <para>
/// A goto loop is the set of statements from which a goto back to its label can be reached again
/// (<see cref="CyclePlanner"/>). A lane that falls out of a statement holding such a goto has left the loop: before
/// the next statement it leaves the regions it is inside (the branch of an <c>if</c>), arrives at the loop's region
/// and enters those regions again, so that it waits there for the lanes that are still in the loop without holding
/// their branches open.
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
    private readonly bool _stackForm;

    // The regions the statement being rewritten is inside, outermost first
    private readonly ImmutableArray<Region>.Builder _open = ImmutableArray.CreateBuilder<Region>();

    // The loops and switch statements the statement being rewritten is inside, outermost first
    private readonly ImmutableArray<JumpTarget>.Builder _targets = ImmutableArray.CreateBuilder<JumpTarget>();

    // The planned natural-loop cycles of the method body being rewritten
    private CyclePlan _cycles = CyclePlan.Empty;

    // The open region stack carried to a label by a reachable jump targeting it
    private readonly Dictionary<LabeledStatementSyntax, ImmutableArray<Region>> _labelEntryOpen = new();

    // Whether control reaches the current statement in the active rewrite walk
    private bool _reaches = true;

    private ReconvergenceInserter(SemanticModel? model, UniformityAnalysis? uniformity,
        MethodDeclarationSyntax? declaration, bool isDeviceFunction, bool stackForm)
    {
        _model = model;
        _uniformity = uniformity;
        _declaration = declaration;
        _isDeviceFunction = isDeviceFunction;
        _stackForm = stackForm;
    }

    /// <summary>
    /// The regions numbered so far: the first warp sites reserved on the frame (unused by regions, kept so collective
    /// sites do not shift).
    /// </summary>
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
    /// <param name="stackForm">
    /// Whether the body runs on a stack of its own (the CPU stack models) rather than as a state machine: the regions
    /// take the warp frame of the stack (<c>__gsharp_warp_frame</c>) instead of the root and the context, and a lane
    /// arrives at, waits at and passes the end of a region in one call (<c>__gsharp_stack_warp_reconverge</c>), which
    /// the state machine splits into an arrive, a state and a pass.
    /// </param>
    public static ReconvergenceInserter? TryCreate(SemanticModel? model, IMethodSymbol? owner, bool isDeviceFunction,
        bool stackForm = false)
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

        return new ReconvergenceInserter(model, uniformity, declaration, isDeviceFunction, stackForm);
    }

    /// <inheritdoc />
    public BlockSyntax Rewrite(BlockSyntax block)
    {
        _labelEntryOpen.Clear();
        _reaches = true;
        _cycles = CyclePlanner.Plan(block, BarrierHelper.ContainsPhaseBarrier, GetConstantCondition);
        return LabeledStatementSplitter.Split(
            block.WithStatements(RewriteList(block.Statements, isKernelBody: !_isDeviceFunction)));
    }

    // isKernelBody: the statements of the kernel body, whose last statement ends the thread
    private SyntaxList<StatementSyntax> RewriteList(
        SyntaxList<StatementSyntax> statements,
        bool isKernelBody,
        JumpRegionPlan? precomputedPlan = null,
        SwitchSectionJoinState? switchJoins = null)
    {
        var plan = precomputedPlan ?? JumpRegionPlanner.Plan(statements, BarrierHelper.ContainsPhaseBarrier);
        if (!plan.Rejected.IsEmpty)
        {
            plan = JumpRegionPlan.Empty;
        }

        var rewritten = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count * 2);
        var switchSegment = 0;
        var nextJoinIndex = 0;
        var entryReaches = _reaches;
        var switchFirstHeld = switchJoins != null ? switchJoins.Plan.FirstHeld[switchJoins.Section] : default;
        if (switchJoins != null)
        {
            for (var order = switchJoins.Plan.Joins.Length; order >= switchFirstHeld[0]; order--)
            {
                OpenSwitchJoin(rewritten, switchJoins, order);
            }

            if (nextJoinIndex < switchJoins.SectionJoins.Length &&
                switchJoins.SectionJoins[nextJoinIndex].Position == 0)
            {
                CloseSwitchJoin(
                    rewritten,
                    switchJoins.SwitchStatement.Sections[switchJoins.Section],
                    switchJoins.SectionJoins[nextJoinIndex],
                    switchFirstHeld,
                    ref switchSegment,
                    switchJoins);
                _reaches = entryReaches;
                nextJoinIndex++;
            }
        }

        // The ranges this list opened; a goto loop closes at its gate statement or after its last statement
        var ranges = new List<Region>(plan.Regions.Length);
        Region? pendingLatchToken = null;
        var pendingLatchIndex = -1;
        for (var i = 0; i < statements.Count; i++)
        {
            if (!_reaches && TryGetLabelEntryOpen(statements, i, out var targetOpen))
            {
                Restore(targetOpen);
                _reaches = true;
            }

            for (var r = ranges.Count - 1; r >= 0; r--)
            {
                if (ranges[r].JumpRegion is { Kind: JumpRegionKind.ForwardRange, LabelIndex: var labelIndex } &&
                    labelIndex == i)
                {
                    CloseRange(rewritten, ranges[r], statements[i]);
                    ranges.RemoveAt(r);
                }
            }

            if (switchJoins != null &&
                nextJoinIndex < switchJoins.SectionJoins.Length &&
                switchJoins.SectionJoins[nextJoinIndex].Position == i + 1)
            {
                CloseSwitchJoin(
                    rewritten,
                    statements[i],
                    switchJoins.SectionJoins[nextJoinIndex],
                    switchFirstHeld,
                    ref switchSegment,
                    switchJoins);
                _reaches = entryReaches;
                nextJoinIndex++;
            }

            EmitGateBefore(rewritten, statements[i]);

            // GotoLoops and any ForwardRanges enclosing them open before the statement's labels (so jumps to those
            // labels stay inside); ForwardRanges inside a GotoLoop (or without one) open after the peeled labels.
            // A loop latch opens right after the last label of the loop's label group, before any inner ForwardRange.
            var statement = statements[i];
            var statementBeforeReaches = _reaches;
            var hasRegionsAtI = plan.Regions
                .AsStackEnumerable()
                .Any(i, static (region, index) => region.First == index);
            var endsThread = isKernelBody && i == statements.Count - 1 && ranges.Count == 0;
            if (hasRegionsAtI)
            {
                var lastLoopAtI = -1;
                for (var p = 0; p < plan.Regions.Length; p++)
                {
                    if (plan.Regions[p].First == i && plan.Regions[p].Kind == JumpRegionKind.GotoLoop)
                    {
                        lastLoopAtI = p;
                    }
                }

                for (var p = 0; p <= lastLoopAtI; p++)
                {
                    if (plan.Regions[p].First == i)
                    {
                        var opened = OpenJumpRegion(
                            rewritten,
                            ranges,
                            statements,
                            plan.Regions[p],
                            includesFirstLabels: true);
                        if (opened.Cycle is { NeedsLatch: true })
                        {
                            pendingLatchToken = opened;
                            pendingLatchIndex = FindLastLabelStatementIndex(statements, plan.Regions[p].LabelIndex);
                        }
                    }
                }

                while (statement is LabeledStatementSyntax labeled)
                {
                    rewritten.Add(labeled.WithStatement(SyntaxFactory.EmptyStatement()));
                    statement = labeled.Statement;
                }

                if (pendingLatchToken != null && pendingLatchIndex == i)
                {
                    OpenLatch(rewritten, pendingLatchToken, statements[i]);
                    pendingLatchToken = null;
                    pendingLatchIndex = -1;
                }

                for (var p = lastLoopAtI + 1; p < plan.Regions.Length; p++)
                {
                    if (plan.Regions[p].First == i)
                    {
                        OpenJumpRegion(rewritten, ranges, statements, plan.Regions[p], includesFirstLabels: false);
                    }
                }

                endsThread = isKernelBody && i == statements.Count - 1 && ranges.Count == 0;
                AddRewritten(rewritten, statement, endsThread);
            }
            else if (pendingLatchToken != null && pendingLatchIndex == i)
            {
                if (JumpRegionPlanner.IsEmptyLabeledStatement(statement))
                {
                    AddRewritten(rewritten, statement, endsThread);
                    OpenLatch(rewritten, pendingLatchToken, statements[i]);
                }
                else
                {
                    while (statement is LabeledStatementSyntax labeled)
                    {
                        rewritten.Add(labeled.WithStatement(SyntaxFactory.EmptyStatement()));
                        statement = labeled.Statement;
                    }

                    OpenLatch(rewritten, pendingLatchToken, statements[i]);
                    AddRewritten(rewritten, statement, endsThread);
                }

                pendingLatchToken = null;
                pendingLatchIndex = -1;
            }
            else
            {
                AddRewritten(rewritten, statement, endsThread);
            }

            _reaches = statementBeforeReaches && ReachesEnd(statements[i]);

            for (var r = ranges.Count - 1; r >= 0; r--)
            {
                if (ranges[r].JumpRegion is { Kind: JumpRegionKind.GotoLoop, Last: var last } jumpRegion && last == i)
                {
                    if (IndexOfRegion(ranges[r]) >= 0)
                    {
                        CloseRange(rewritten, ranges[r], statements[jumpRegion.First]);
                    }

                    ranges.RemoveAt(r);
                }
            }
        }

        if (switchJoins != null)
        {
            var baseDepth = DepthAbove(switchJoins.Below);
            while (_open.Count > baseDepth)
            {
                if (_open[_open.Count - 1].OwnerSwitch != switchJoins.SwitchStatement)
                {
                    throw new InvalidOperationException(
                        "A region inside a switch section was not closed before the section ended.");
                }

                _open.RemoveAt(_open.Count - 1);
            }
        }

        return SyntaxFactory.List(rewritten);
    }

    private void OpenSwitchJoin(
        ImmutableArray<StatementSyntax>.Builder rewritten,
        SwitchSectionJoinState switchJoins,
        int order)
    {
        var join = switchJoins.Plan.Joins[order - 1];
        var site = switchJoins.Sites[order - 1];
        _open.Add(
            new Region(
                site,
                switchJoins.SwitchStatement,
                SwitchJoin: join,
                OwnerSwitch: switchJoins.SwitchStatement,
                SwitchJoinPlan: switchJoins.Plan));
        rewritten.Add(CreateRegionCall(RegionCall.Enter, site));
    }

    private void CloseSwitchJoin(
        ImmutableArray<StatementSyntax>.Builder rewritten,
        SyntaxNode placeNode,
        SwitchJoin join,
        ImmutableArray<int> firstHeld,
        ref int segment,
        SwitchSectionJoinState switchJoins)
    {
        var prevHeld = firstHeld[segment];
        for (var order = prevHeld; order < join.Order; order++)
        {
            PopAndLeaveSwitchJoin(rewritten, switchJoins.SwitchStatement, order);
        }

        if (_open.Count == 0 ||
            _open[_open.Count - 1].OwnerSwitch != switchJoins.SwitchStatement ||
            _open[_open.Count - 1].SwitchJoin != join)
        {
            throw new InvalidOperationException("A switch join is not the innermost open region where it arrives.");
        }

        var site = _open[_open.Count - 1].Site;
        rewritten.Add(
            LoweringSyntax.EmptyLabel($"{GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix}{site}"));
        rewritten.Add(CreateArrive(site, placeNode));
        _open.RemoveAt(_open.Count - 1);

        segment++;
        var nextHeld = firstHeld[segment];
        for (var order = join.Order + 1; order < nextHeld; order++)
        {
            PopAndLeaveSwitchJoin(rewritten, switchJoins.SwitchStatement, order);
        }
    }

    private void PopAndLeaveSwitchJoin(
        ImmutableArray<StatementSyntax>.Builder rewritten,
        SwitchStatementSyntax ownerSwitch,
        int expectedOrder)
    {
        if (_open.Count == 0 ||
            _open[_open.Count - 1].OwnerSwitch != ownerSwitch ||
            _open[_open.Count - 1].SwitchJoin?.Order != expectedOrder)
        {
            throw new InvalidOperationException("A held switch join is not on top of the open region stack.");
        }

        rewritten.Add(CreateRegionCall(RegionCall.Leave, _open[_open.Count - 1].Site));
        _open.RemoveAt(_open.Count - 1);
    }

    private Region OpenJumpRegion(
        ImmutableArray<StatementSyntax>.Builder rewritten,
        List<Region> ranges,
        SyntaxList<StatementSyntax> statements,
        JumpRegion region,
        bool includesFirstLabels)
    {
        var cycle = region.Kind == JumpRegionKind.GotoLoop ? _cycles.Find(statements, region) : null;
        var range = new Region(
            RegionCount++,
            statements[region.First],
            region,
            statements,
            includesFirstLabels,
            Cycle: cycle);
        _open.Add(range);
        ranges.Add(range);
        rewritten.Add(CreateRegionCall(RegionCall.Enter, range.Site));
        return range;
    }

    private static int FindLastLabelStatementIndex(SyntaxList<StatementSyntax> statements, int labelIndex)
    {
        var last = labelIndex;
        while (last + 1 < statements.Count &&
               JumpRegionPlanner.IsEmptyLabeledStatement(statements[last]) &&
               statements[last + 1] is LabeledStatementSyntax)
        {
            last++;
        }

        return last;
    }

    private void OpenLatch(
        ImmutableArray<StatementSyntax>.Builder rewritten,
        Region loopToken,
        StatementSyntax extent)
    {
        if (loopToken.Cycle is not { NeedsLatch: true } cycle)
        {
            return;
        }

        var latch = new Region(
            RegionCount++,
            extent,
            Cycle: cycle,
            IsLatch: true);
        _open.Add(latch);
        rewritten.Add(CreateRegionCall(RegionCall.Enter, latch.Site));
    }

    // The first statement of a goto loop, whose place names the loop's region in the messages
    private static StatementSyntax GetLoopStart(Region range) =>
        range.JumpRegion is { } jumpRegion
            ? range.EnclosingStatements[jumpRegion.First]
            : throw new InvalidOperationException("A goto loop has no jump region.");

    // Closes the outermost open goto loop token whose gate statement is statement
    private void EmitGateBefore(ImmutableArray<StatementSyntax>.Builder statements, StatementSyntax statement)
    {
        for (var i = 0; i < _open.Count; i++)
        {
            if (!_open[i].IsLatch &&
                _open[i].Cycle is { } cycle &&
                _cycles.IsGateStatement(cycle, statement))
            {
                EmitGate(statements, _open[i]);
                return;
            }
        }
    }

    // Leaves everything above token, arrives at token, and enters the structural regions above it again
    private void EmitGate(ImmutableArray<StatementSyntax>.Builder statements, Region token)
    {
        var index = IndexOfRegion(token);
        if (index < 0)
        {
            throw new InvalidOperationException("A goto loop token is not open where its gate closes it.");
        }

        if (token.IsLatch)
        {
            throw new InvalidOperationException("A gate closes a goto loop token, not its latch.");
        }

        if (token.JumpRegion?.Kind == JumpRegionKind.ForwardRange)
        {
            throw new InvalidOperationException("A gate closes a goto loop, not a forward range.");
        }

        var above = ImmutableArray.CreateBuilder<Region>(_open.Count - index - 1);
        for (var i = _open.Count - 1; i > index; i--)
        {
            statements.Add(CreateRegionCall(RegionCall.Leave, _open[i].Site));
            above.Add(_open[i]);
        }

        statements.Add(CreateArrive(token.Site, GetPlaceNode(GetLoopStart(token))));
        while (_open.Count > index)
        {
            _open.RemoveAt(_open.Count - 1);
        }

        for (var i = above.Count - 1; i >= 0; i--)
        {
            if (above[i].JumpRegion?.Kind == JumpRegionKind.GotoLoop || above[i].IsLoopBody || above[i].IsLatch)
            {
                continue;
            }

            statements.Add(CreateRegionCall(RegionCall.Enter, above[i].Site));
            _open.Add(above[i]);
        }
    }

    // __gsharp_ReconvergeJoin_<site>: ; (for ForwardRange) and __gsharp_warp_reconverge_arrive(...); where a jump
    // region ends
    private void CloseRange(
        ImmutableArray<StatementSyntax>.Builder statements,
        Region range,
        StatementSyntax placeStatement)
    {
        if (range.JumpRegion?.Kind == JumpRegionKind.GotoLoop)
        {
            EmitGate(statements, range);
            return;
        }

        if (_open.Count == 0 || !ReferenceEquals(_open[_open.Count - 1], range))
        {
            throw new InvalidOperationException("A jump range is not the innermost open region where it ends");
        }

        if (range.JumpRegion?.Kind == JumpRegionKind.ForwardRange)
        {
            statements.Add(
                LoweringSyntax.EmptyLabel($"{GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix}{range.Site}"));
        }

        statements.Add(CreateArrive(range.Site, GetPlaceNode(placeStatement)));
        _open.RemoveAt(_open.Count - 1);
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
            var plain = RewriteStatement(statement, endsThread, statementRegion: null, out var plainAfter);
            statements.Add(plain);
            statements.AddRange(plainAfter);
            return;
        }

        statements.Add(CreateRegionCall(RegionCall.Enter, region.Site));
        var rewritten = RewriteStatement(statement, endsThread: false, region, out var afterStatements);
        statements.Add(rewritten);
        statements.AddRange(afterStatements);
        PopRegion(region);
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
    private StatementSyntax RewriteStatement(
        StatementSyntax statement,
        bool endsThread,
        Region? statementRegion,
        out ImmutableArray<StatementSyntax> afterStatements)
    {
        afterStatements = ImmutableArray<StatementSyntax>.Empty;
        switch (statement)
        {
            case BlockSyntax block:
                return block.WithStatements(RewriteList(block.Statements, isKernelBody: endsThread));
            case IfStatementSyntax ifStatement:
                return RewriteIf(ifStatement);
            case SwitchStatementSyntax switchStatement:
                return RewriteSwitch(switchStatement);
            case WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax:
                return RewriteLoop(statement, statementRegion, out afterStatements);
            case LabeledStatementSyntax labeled:
                return labeled.WithStatement(RewriteEmbedded(labeled.Statement));
            case UnsafeStatementSyntax unsafeStatement:
                return unsafeStatement.WithBlock(
                    unsafeStatement.Block.WithStatements(RewriteList(unsafeStatement.Block.Statements, isKernelBody: false)));
            case CheckedStatementSyntax checkedStatement:
                return checkedStatement.WithBlock(
                    checkedStatement.Block.WithStatements(RewriteList(checkedStatement.Block.Statements, isKernelBody: false)));
            default:
                return statement;
        }
    }

    private IfStatementSyntax RewriteIf(IfStatementSyntax ifStatement)
    {
        var before = _open.ToImmutable();
        var beforeReaches = _reaches;
        var condition = GetConstantCondition(ifStatement.Condition);

        _reaches = beforeReaches && condition != false;
        var thenBranch = RewriteEmbedded(ifStatement.Statement);
        var thenReachesEnd = ReachesEnd(ifStatement.Statement);
        var thenOpen = thenReachesEnd ? _open.ToImmutable() : default;

        Restore(before);
        _reaches = beforeReaches && condition != true;
        var elseBranch = ifStatement.Else is { } elseClause ? RewriteEmbedded(elseClause.Statement) : null;
        var elseReachesEnd = ifStatement.Else == null || ReachesEnd(ifStatement.Else.Statement);
        var elseOpen = elseReachesEnd ? _open.ToImmutable() : default;

        if (thenReachesEnd && elseReachesEnd)
        {
            if (!RegionsEqual(thenOpen, elseOpen))
            {
                if (thenOpen.Length == elseOpen.Length)
                {
                    throw new InvalidOperationException("The branches of an if end with different regions.");
                }

                var thenIsLonger = thenOpen.Length > elseOpen.Length;
                var longer = thenIsLonger ? thenOpen : elseOpen;
                var shorter = thenIsLonger ? elseOpen : thenOpen;
                var token = FindLowestClosedCycleToken(longer, shorter)
                    ?? throw new InvalidOperationException("The branches of an if end with different regions.");

                Restore(longer);
                var gateStatements = ImmutableArray.CreateBuilder<StatementSyntax>();
                EmitGate(gateStatements, token);
                if (!OpenMatches(shorter))
                {
                    throw new InvalidOperationException("The branches of an if end with different regions.");
                }

                if (thenIsLonger)
                {
                    thenBranch = AppendStatements(thenBranch, gateStatements);
                }
                else
                {
                    elseBranch = elseBranch != null
                        ? AppendStatements(elseBranch, gateStatements)
                        : SyntaxFactory.Block(gateStatements);
                }
            }
            else
            {
                Restore(thenOpen);
            }
        }
        else if (thenReachesEnd)
        {
            Restore(thenOpen);
        }
        else if (elseReachesEnd)
        {
            Restore(elseOpen);
        }
        else
        {
            Restore(before);
        }

        var rewritten = ifStatement.WithStatement(thenBranch);
        if (elseBranch != null)
        {
            rewritten = ifStatement.Else is { } existingElse
                ? rewritten.WithElse(existingElse.WithStatement(elseBranch))
                : rewritten.WithElse(SyntaxFactory.ElseClause(elseBranch));
        }

        _reaches = beforeReaches && ReachesEnd(ifStatement);
        return rewritten;
    }

    private void Restore(ImmutableArray<Region> regions)
    {
        _open.Clear();
        _open.AddRange(regions);
    }

    private bool ReachesEnd(StatementSyntax statement) =>
        StatementFlow.ReachesEnd(statement, GetConstantCondition);

    private ImmutableArray<Region> SliceOpen(int count)
    {
        if (count >= _open.Count)
        {
            return _open.ToImmutable();
        }

        var builder = ImmutableArray.CreateBuilder<Region>(count);
        for (var i = 0; i < count; i++)
        {
            builder.Add(_open[i]);
        }

        return builder.ToImmutable();
    }

    private bool TryGetLabelEntryOpen(
        SyntaxList<StatementSyntax> statements,
        int index,
        out ImmutableArray<Region> open)
    {
        if (statements[index] is not LabeledStatementSyntax)
        {
            open = default;
            return false;
        }

        var lastLabelIndex = FindLastLabelStatementIndex(statements, index);
        for (var k = index; k <= lastLabelIndex; k++)
        {
            for (var cur = statements[k]; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
            {
                if (_labelEntryOpen.TryGetValue(labeled, out open))
                {
                    return true;
                }
            }
        }

        open = default;
        return false;
    }

    private static bool RegionsEqual(ImmutableArray<Region> left, ImmutableArray<Region> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool OpenMatches(ImmutableArray<Region> expected)
    {
        if (_open.Count != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!ReferenceEquals(_open[i], expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static Region? FindLowestClosedCycleToken(ImmutableArray<Region> longer, ImmutableArray<Region> shorter)
    {
        for (var i = 0; i < longer.Length; i++)
        {
            var candidate = longer[i];
            if (!candidate.IsLatch &&
                candidate.Cycle != null &&
                !shorter
                    .AsStackEnumerable()
                    .Any(candidate, static (region, token) => ReferenceEquals(region, token)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static StatementSyntax AppendStatements(
        StatementSyntax branch,
        ImmutableArray<StatementSyntax>.Builder extra)
    {
        var additions = extra.ToImmutable();
        if (branch is BlockSyntax block)
        {
            return block.WithStatements(block.Statements.AddRange(additions));
        }

        var combined = ImmutableArray.CreateBuilder<StatementSyntax>(additions.Length + 1);
        combined.Add(branch);
        combined.AddRange(additions);
        return SyntaxFactory.Block(combined);
    }

    private SwitchStatementSyntax RewriteSwitch(SwitchStatementSyntax switchStatement)
    {
        var sectionPlans = new JumpRegionPlan[switchStatement.Sections.Count];
        for (var s = 0; s < switchStatement.Sections.Count; s++)
        {
            sectionPlans[s] = JumpRegionPlanner.Plan(
                switchStatement.Sections[s].Statements, BarrierHelper.ContainsPhaseBarrier);
        }

        var crossSectionRejected = JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, sectionPlans);

        var joinPlan = SwitchJoinPlan.Empty;
        Func<GotoStatementSyntax, int>? sectionLookup = null;
        int[]? joinSites = null;
        if (crossSectionRejected.IsEmpty &&
            SwitchStatementFlattener.CanFlatten(switchStatement) &&
            switchStatement.Sections
                .AsStackEnumerable()
                .Any(BarrierHelper.ContainsPhaseBarrier) &&
            IsThreadVariant(switchStatement.Expression))
        {
            sectionLookup = SwitchCaseLookup.CreateSectionLookup(switchStatement, _model);
            joinPlan = SwitchJoinPlanner.Plan(switchStatement, sectionLookup);
            if (!joinPlan.Joins.IsEmpty)
            {
                joinSites = new int[joinPlan.Joins.Length];
                for (var j = joinPlan.Joins.Length - 1; j >= 0; j--)
                {
                    joinSites[j] = RegionCount++;
                }
            }
        }

        var below = _open.Count > 0 ? _open[_open.Count - 1] : null;
        _targets.Add(new JumpTarget(
            switchStatement,
            IsLoop: false,
            below,
            BodyRegion: null,
            ContinueLabel: null,
            SectionLookup: joinSites != null ? sectionLookup : null));
        var before = _open.ToImmutable();
        var beforeReaches = _reaches;
        var sections = ImmutableArray.CreateBuilder<SwitchSectionSyntax>(switchStatement.Sections.Count);
        for (var s = 0; s < switchStatement.Sections.Count; s++)
        {
            Restore(before);
            _reaches = beforeReaches;
            var sectionPlan = crossSectionRejected.IsEmpty ? sectionPlans[s] : JumpRegionPlan.Empty;
            SwitchSectionJoinState? sectionJoins = null;
            if (joinSites != null)
            {
                var joinsBuilder = ImmutableArray.CreateBuilder<SwitchJoin>();
                joinPlan.Joins
                    .AsStackEnumerable()
                    .Where(s, static (join, sec) => join.Section == sec)
                    .AddTo(joinsBuilder);
                sectionJoins = new SwitchSectionJoinState(
                    switchStatement,
                    s,
                    below,
                    joinPlan,
                    joinsBuilder.ToImmutable(),
                    joinSites);
            }

            sections.Add(
                switchStatement.Sections[s].WithStatements(
                    RewriteList(
                        switchStatement.Sections[s].Statements,
                        isKernelBody: false,
                        sectionPlan,
                        sectionJoins)));
        }

        Restore(before);
        _reaches = beforeReaches && ReachesEnd(switchStatement);
        _targets.RemoveAt(_targets.Count - 1);
        return switchStatement.WithSections(SyntaxFactory.List(sections));
    }

    // A loop whose body some lanes continue gets a region around its body, which ends where the body ends; the continue
    // statements go there: { __gsharp_warp_region_enter(...); <body> __gsharp_ReconvergeContinue_<n>: ; __gsharp_warp_reconverge_arrive(...); }
    private StatementSyntax RewriteLoop(
        StatementSyntax loop,
        Region? region,
        out ImmutableArray<StatementSyntax> afterLoop)
    {
        var body = CyclePlanner.GetLoopBody(loop);
        var dropped = _cycles.FindCyclesGatedInside(loop);
        var openDropped = FindOutermostOpenCycleToken(dropped) != null;
        if (openDropped && region == null)
        {
            throw new InvalidOperationException(
                "A loop that closes an enclosing goto loop must have a loop region.");
        }

        var hasNaturalLoop = openDropped && _cycles.HasNaturalLoop(loop);
        var joinLabel = hasNaturalLoop && region != null
            ? $"{GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix}{region.Site}"
            : null;
        var targetDropped = hasNaturalLoop ? dropped : default;

        var below = _open.Count > 0 ? _open[_open.Count - 1] : null;
        var bodyRegion = (BarrierHelper.ContainsPhaseBarrier(body) || openDropped) && HasContinue(loop)
            ? OpenRegion(body, isLoopBody: true)
            : null;
        var continueLabel = bodyRegion != null
            ? $"{GSharpInternalNames.Lowering.ReconvergeContinueLabelPrefix}{bodyRegion.Site}"
            : null;
        _targets.Add(new JumpTarget(
            loop,
            IsLoop: true,
            below,
            bodyRegion,
            continueLabel,
            JoinLabel: joinLabel,
            Dropped: targetDropped));

        var bodyEntryOpen = _open.ToImmutable();
        var beforeReaches = _reaches;
        if (!beforeReaches ||
            (loop is WhileStatementSyntax whileLoop && GetConstantCondition(whileLoop.Condition) == false) ||
            (loop is ForStatementSyntax forLoop &&
             forLoop.Condition != null &&
             GetConstantCondition(forLoop.Condition) == false))
        {
            _reaches = false;
        }

        StatementSyntax rewrittenBody;
        if (bodyRegion != null && continueLabel != null)
        {
            var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
            statements.Add(CreateRegionCall(RegionCall.Enter, bodyRegion.Site));
            var bodyStatements = body is BlockSyntax block ? block.Statements : SyntaxFactory.SingletonList(body);
            statements.AddRange(RewriteList(bodyStatements, isKernelBody: false));
            statements.Add(LoweringSyntax.EmptyLabel(continueLabel));
            statements.Add(CreateArrive(bodyRegion.Site, loop));
            rewrittenBody = body is BlockSyntax bodyBlock
                ? bodyBlock.WithStatements(SyntaxFactory.List(statements))
                : SyntaxFactory.Block(statements);
        }
        else
        {
            rewrittenBody = RewriteEmbedded(body);
        }

        _targets.RemoveAt(_targets.Count - 1);

        var afterBuilder = ImmutableArray.CreateBuilder<StatementSyntax>();
        if (openDropped)
        {
            var bodyReachesEnd = StatementFlow.ReachesEnd(body, GetConstantCondition);
            if (hasNaturalLoop)
            {
                if (bodyReachesEnd)
                {
                    if (!OpenMatches(bodyEntryOpen))
                    {
                        throw new InvalidOperationException(
                            "The body of a loop ends with other regions than it starts with.");
                    }
                }
                else
                {
                    Restore(bodyEntryOpen);
                }

                if (bodyRegion != null)
                {
                    PopRegion(bodyRegion);
                }

                if (FindOutermostOpenCycleToken(dropped) is { } token)
                {
                    EmitGate(afterBuilder, token);
                }

                if (joinLabel != null)
                {
                    afterBuilder.Add(LoweringSyntax.EmptyLabel(joinLabel));
                }
            }
            else
            {
                if (!bodyReachesEnd)
                {
                    Restore(bodyEntryOpen);
                }

                if (bodyRegion != null)
                {
                    PopRegion(bodyRegion);
                }
            }
        }
        else
        {
            if (!OpenMatches(bodyEntryOpen))
            {
                if (StatementFlow.ReachesEnd(body, GetConstantCondition))
                {
                    throw new InvalidOperationException(
                        "The body of a loop ends with other regions than it starts with.");
                }

                Restore(bodyEntryOpen);
            }

            if (bodyRegion != null)
            {
                PopRegion(bodyRegion);
            }
        }

        _reaches = beforeReaches && ReachesEnd(loop);
        afterLoop = afterBuilder.ToImmutable();
        return loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.WithStatement(rewrittenBody),
            DoStatementSyntax doStatement => doStatement.WithStatement(rewrittenBody),
            ForStatementSyntax forStatement => forStatement.WithStatement(rewrittenBody),
            CommonForEachStatementSyntax forEach => forEach.WithStatement(rewrittenBody),
            _ => throw new InvalidOperationException($"Not a loop: {loop.Kind()}")
        };
    }

    // A jump out of regions leaves them, innermost first; a continue of a loop with a body region goes to its end
    private bool TryRewriteJump(StatementSyntax statement, ImmutableArray<StatementSyntax>.Builder statements)
    {
        int leaveFrom;
        StatementSyntax jump = statement;
        ImmutableArray<Region> savedOpen = default;
        switch (statement)
        {
            case BreakStatementSyntax when FindTarget(statement, loopOnly: false) is { } target:
                if (target.JoinLabel != null)
                {
                    if (!target.Dropped.IsDefaultOrEmpty &&
                        FindOutermostOpenCycleToken(target.Dropped) is { } token)
                    {
                        savedOpen = _open.ToImmutable();
                        EmitGate(statements, token);
                    }

                    jump = LoweringSyntax.Goto(target.JoinLabel)
                        .WithTriviaFrom(statement)
                        .WithOriginalSpanFrom(statement);
                }

                leaveFrom = DepthAbove(target.Below);
                break;
            case ContinueStatementSyntax when FindTarget(statement, loopOnly: true) is { } target:
                leaveFrom = DepthAbove(target.BodyRegion ?? target.Below);
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
                for (var r = _open.Count - 1; r >= 0; r--)
                {
                    if (_open[r] is { IsLatch: true, Cycle: { } cycle } latch &&
                        cycle.BackJumps
                            .AsStackEnumerable()
                            .Any(gotoStatement, static (backJump, current) => ReferenceEquals(backJump, current)))
                    {
                        for (var i = _open.Count - 1; i > r; i--)
                        {
                            statements.Add(CreateRegionCall(RegionCall.Leave, _open[i].Site));
                        }

                        statements.Add(CreateArrive(latch.Site, GetPlaceNode(cycle.Header)));
                        statements.Add(jump);
                        return true;
                    }
                }

                leaveFrom = -1;
                LabeledStatementSyntax? recordedTarget = null;
                if (gotoStatement.Expression is IdentifierNameSyntax { Identifier.ValueText: var targetName } &&
                    ReconvergenceSyntax.FindLabel(gotoStatement, targetName) is { } targetLabel)
                {
                    recordedTarget = targetLabel;
                    var rewroteToJoin = false;
                    for (var r = _open.Count - 1; r >= 0; r--)
                    {
                        if (_open[r].JumpRegion is { Kind: JumpRegionKind.ForwardRange } fr &&
                            IsTargetLabel(fr, targetLabel, _open[r].EnclosingStatements))
                        {
                            jump = LoweringSyntax.Goto(
                                    $"{GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix}{_open[r].Site}")
                                .WithTriviaFrom(statement)
                                .WithOriginalSpanFrom(statement);

                            // The lane arrives at the join of the range, so it keeps the range and every region below
                            // it, the switch joins it still holds included
                            leaveFrom = r + 1;
                            rewroteToJoin = true;
                            break;
                        }
                    }

                    if (!rewroteToJoin)
                    {
                        for (var r = _open.Count - 1; r >= 0; r--)
                        {
                            if (_open[r].SwitchJoin is { Position: > 0 } switchJoin &&
                                _open[r].OwnerSwitch is { } ownerSwitch &&
                                IsLabelAtCanonicalIndex(
                                    ownerSwitch.Sections[switchJoin.Section].Statements,
                                    switchJoin.Position - 1,
                                    targetLabel))
                            {
                                jump = LoweringSyntax.Goto(
                                        $"{GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix}{_open[r].Site}")
                                    .WithTriviaFrom(statement)
                                    .WithOriginalSpanFrom(statement);
                                break;
                            }
                        }
                    }
                }

                if (leaveFrom < 0)
                {
                    leaveFrom = FindLabelDepth(gotoStatement);
                }

                if (_reaches && recordedTarget != null)
                {
                    _labelEntryOpen[recordedTarget] = SliceOpen(leaveFrom);
                }

                break;
            case GotoStatementSyntax gotoCase when FindSwitch() is { } target:
                var switchBaseDepth = DepthAbove(target.Below);
                leaveFrom = switchBaseDepth;
                if (target.Node is SwitchStatementSyntax switchOwner &&
                    target.SectionLookup != null)
                {
                    var targetSection = target.SectionLookup(gotoCase);
                    if (targetSection >= 0)
                    {
                        for (var r = _open.Count - 1; r >= switchBaseDepth; r--)
                        {
                            if (_open[r].OwnerSwitch == switchOwner &&
                                _open[r].SwitchJoin is { Position: 0, Section: var section } &&
                                section == targetSection)
                            {
                                jump = LoweringSyntax.Goto(
                                        $"{GSharpInternalNames.Lowering.ReconvergeJoinLabelPrefix}{_open[r].Site}")
                                    .WithTriviaFrom(statement)
                                    .WithOriginalSpanFrom(statement);
                                leaveFrom = r + 1;
                                break;
                            }
                        }
                    }
                }

                break;
            default:
                return false;
        }

        for (var i = _open.Count - 1; i >= leaveFrom; i--)
        {
            statements.Add(CreateRegionCall(RegionCall.Leave, _open[i].Site));
        }

        if (!savedOpen.IsDefault)
        {
            Restore(savedOpen);
        }

        statements.Add(jump);
        return true;
    }

    // The target that the break or continue leaves: the one it names when it has a label, otherwise the innermost
    private JumpTarget? FindTarget(StatementSyntax jump, bool loopOnly)
    {
        var named = LabeledJumpSyntax.GetLabelName(jump) != null
            ? ReconvergenceSyntax.FindJumpTarget(jump, loopOnly)
            : null;
        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            if (named != null ? _targets[i].Node == named : !loopOnly || _targets[i].IsLoop)
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
        while (depth < _open.Count && RegionContainsLabel(_open[depth], label))
        {
            depth++;
        }

        return depth;
    }

    private static bool RegionContainsLabel(Region region, LabeledStatementSyntax label)
    {
        if (region.SwitchJoin is { } switchJoin &&
            region.OwnerSwitch is { } ownerSwitch &&
            region.SwitchJoinPlan is { } joinPlan)
        {
            if (!ReconvergenceSyntax.IsInside(label, ownerSwitch))
            {
                return false;
            }

            foreach (var join in joinPlan.Joins)
            {
                if (join.Position > 0 &&
                    IsLabelAtCanonicalIndex(
                        ownerSwitch.Sections[join.Section].Statements,
                        join.Position - 1,
                        label))
                {
                    return switchJoin.Order >= join.Order;
                }
            }

            for (var s = 0; s < ownerSwitch.Sections.Count; s++)
            {
                var statements = ownerSwitch.Sections[s].Statements;
                for (var i = 0; i < statements.Count; i++)
                {
                    if (ReconvergenceSyntax.IsInside(label, statements[i]))
                    {
                        var segment = SwitchJoinPlanner.FindSegmentForStatement(joinPlan.Joins, s, i);
                        return switchJoin.Order >= joinPlan.FirstHeld[s][segment];
                    }
                }
            }

            return false;
        }

        if (region.IsLatch && region.Cycle is { } latchCycle)
        {
            if (IsLabelAtCanonicalIndex(latchCycle.Statements, latchCycle.Region.LabelIndex, label))
            {
                return false;
            }

            for (var i = latchCycle.Region.First; i <= latchCycle.Region.Last; i++)
            {
                if (ReconvergenceSyntax.IsInside(label, latchCycle.Statements[i]))
                {
                    return true;
                }
            }

            return false;
        }

        if (region.JumpRegion is { } jumpRegion)
        {
            if (jumpRegion.Kind == JumpRegionKind.ForwardRange &&
                IsTargetLabel(jumpRegion, label, region.EnclosingStatements))
            {
                return true;
            }

            // Labels on the first statement are outside only when the region opened after them
            for (var i = jumpRegion.First; i <= jumpRegion.Last; i++)
            {
                var statement = region.EnclosingStatements[i];
                if (i == jumpRegion.First && !region.IncludesFirstLabels)
                {
                    while (statement is LabeledStatementSyntax labeled)
                    {
                        statement = labeled.Statement;
                    }
                }

                if (ReconvergenceSyntax.IsInside(label, statement))
                {
                    return true;
                }
            }

            return false;
        }

        return ReconvergenceSyntax.IsInside(label, region.Extent);
    }

    private static bool IsTargetLabel(
        JumpRegion region,
        LabeledStatementSyntax label,
        SyntaxList<StatementSyntax> statements) =>
        IsLabelAtCanonicalIndex(statements, region.LabelIndex, label);

    private static bool IsLabelAtCanonicalIndex(
        SyntaxList<StatementSyntax> statements,
        int canonicalIndex,
        LabeledStatementSyntax label)
    {
        if (canonicalIndex < 0 || canonicalIndex >= statements.Count)
        {
            return false;
        }

        for (var i = canonicalIndex; i < statements.Count; i++)
        {
            if (IsLabelAttachedTo(statements[i], label))
            {
                return true;
            }

            if (!JumpRegionPlanner.IsEmptyLabeledStatement(statements[i]))
            {
                break;
            }
        }

        return false;
    }

    private static bool IsLabelAttachedTo(StatementSyntax statement, LabeledStatementSyntax label)
    {
        for (var cur = statement; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
        {
            if (labeled == label)
            {
                return true;
            }
        }

        return false;
    }

    private Region OpenRegion(SyntaxNode extent, bool isLoopBody = false)
    {
        var region = new Region(RegionCount++, extent, IsLoopBody: isLoopBody);
        _open.Add(region);
        return region;
    }

    private void PopRegion(Region expected)
    {
        if (_open.Count == 0 || !ReferenceEquals(_open[_open.Count - 1], expected))
        {
            throw new InvalidOperationException("A region is not the innermost open region where it ends.");
        }

        _open.RemoveAt(_open.Count - 1);
    }

    private int DepthAbove(Region? below)
    {
        if (below == null)
        {
            return 0;
        }

        var index = IndexOfRegion(below);
        if (index < 0)
        {
            throw new InvalidOperationException("A region that a jump target was opened above is no longer open.");
        }

        return index + 1;
    }

    private int IndexOfRegion(Region region)
    {
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_open[i], region))
            {
                return i;
            }
        }

        return -1;
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
                (IsThreadVariant(switchStatement.Expression) || HasWhenClause(switchStatement) ||
                 ReconvergenceSyntax.HasNestedExit(switchStatement)),
            WhileStatementSyntax whileStatement =>
                (BarrierHelper.ContainsPhaseBarrier(whileStatement) &&
                 (IsThreadVariant(whileStatement.Condition) || HasExit(whileStatement))) ||
                HasOpenGatedCycle(whileStatement),
            DoStatementSyntax doStatement =>
                (BarrierHelper.ContainsPhaseBarrier(doStatement) &&
                 (IsThreadVariant(doStatement.Condition) || HasExit(doStatement))) ||
                HasOpenGatedCycle(doStatement),
            ForStatementSyntax forStatement =>
                (BarrierHelper.ContainsPhaseBarrier(forStatement.Statement) &&
                 ((forStatement.Condition is { } condition && IsThreadVariant(condition)) || HasExit(forStatement))) ||
                HasOpenGatedCycle(forStatement),
            CommonForEachStatementSyntax forEach =>
                (BarrierHelper.ContainsPhaseBarrier(forEach.Statement) &&
                 (IsThreadVariant(forEach.Expression) || HasExit(forEach))) ||
                HasOpenGatedCycle(forEach),
            _ => false
        };

    private bool HasOpenGatedCycle(StatementSyntax loop) =>
        FindOutermostOpenCycleToken(_cycles.FindCyclesGatedInside(loop)) != null;

    private Region? FindOutermostOpenCycleToken(ImmutableArray<Cycle> cycles)
    {
        if (cycles.IsDefaultOrEmpty)
        {
            return null;
        }

        for (var i = 0; i < _open.Count; i++)
        {
            if (!_open[i].IsLatch &&
                _open[i].Cycle is { } cycle &&
                cycles
                    .AsStackEnumerable()
                    .Any(cycle, static (candidate, target) => ReferenceEquals(candidate, target)))
            {
                return _open[i];
            }
        }

        return null;
    }

    private static bool HasWhenClause(SwitchStatementSyntax switchStatement) =>
        switchStatement.Sections
            .AsStackEnumerable()
            .Any(static section => section.Labels
                .AsStackEnumerable()
                .Any(static label => label is CasePatternSwitchLabelSyntax { WhenClause: not null })
            );

    // A break of the loop, a labeled break or continue to a statement around it, or a return or goto in its body, by which
    // a lane may leave the loop before the others
    private static bool HasExit(StatementSyntax loop) =>
        CyclePlanner.GetLoopBody(loop)
            .DescendantNodesAndSelf(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .AsStackEnumerable()
            .Any(loop, static (node, target) =>
                node is ReturnStatementSyntax ||
                node.IsKind(SyntaxKind.GotoStatement) ||
                ReconvergenceSyntax.LeavesLoop(node, target));

    // Whether a continue goes to the end of the body of loop
    private static bool HasContinue(StatementSyntax loop) =>
        CyclePlanner.GetLoopBody(loop)
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

    private bool? GetConstantCondition(ExpressionSyntax condition)
    {
        if (condition is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.TrueLiteralExpression))
            {
                return true;
            }

            if (literal.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                return false;
            }
        }

        if (_model != null &&
            _model.TryGetOriginalNode(condition, out var original, out var originalModel) &&
            originalModel.GetConstantValue(original) is { HasValue: true, Value: bool value })
        {
            return value;
        }

        return null;
    }

    // __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, <site>); or, in the stack form,
    // __gsharp_stack_warp_region_enter(__gsharp_warp_frame, <site>); (leave alike)
    private ExpressionStatementSyntax CreateRegionCall(RegionCall call, int site)
    {
        var function = (call, _stackForm) switch
        {
            (RegionCall.Enter, false) => GSharpInternalNames.Prelude.WarpRegionEnter,
            (RegionCall.Leave, false) => GSharpInternalNames.Prelude.WarpRegionLeave,
            (RegionCall.Enter, true) => GSharpInternalNames.StackWarp.RegionEnter,
            _ => GSharpInternalNames.StackWarp.RegionLeave
        };
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(function),
                SyntaxFactory.ArgumentList(CreateRegionArguments(site))
            )
        );
    }

    // __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, <site>, "<place>"); a split point, or, in the stack
    // form, __gsharp_stack_warp_reconverge(__gsharp_warp_frame, <site>, "<place>"); which arrives, waits and passes
    private ExpressionStatementSyntax CreateArrive(int site, SyntaxNode construct)
    {
        var place = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(SourcePlace.Format(_model, construct))
        );
        var arrive = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(_stackForm
                ? GSharpInternalNames.StackWarp.Reconverge
                : GSharpInternalNames.Prelude.WarpReconvergeArrive),
            SyntaxFactory.ArgumentList(CreateRegionArguments(site).Add(SyntaxFactory.Argument(place)))
        ).WithAdditionalAnnotations(
            new SyntaxAnnotation(LoweringAnnotations.SyncIntrinsicAnnotationKind, LoweringAnnotations.ReconvergeAnnotationData)
        );
        return SyntaxFactory.ExpressionStatement(arrive);
    }

    // __gsharp_root, __gsharp_ctx, <site>; or, in the stack form, __gsharp_warp_frame, <site>
    private SeparatedSyntaxList<ArgumentSyntax> CreateRegionArguments(int site)
    {
        var siteArgument = SyntaxFactory.Argument(
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(site))
        );
        return _stackForm
            ? SyntaxFactory.SeparatedList<ArgumentSyntax>(
            [
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.StackWarp.Frame)),
                siteArgument
            ])
            : SyntaxFactory.SeparatedList<ArgumentSyntax>(
            [
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Root)),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context)),
                siteArgument
            ]);
    }

    private enum RegionCall
    {
        Enter,
        Leave
    }

    // A region: its number (the warp site of its entry count) and the statement it covers
    private sealed record Region(
        int Site,
        SyntaxNode Extent,
        JumpRegion? JumpRegion = null,
        SyntaxList<StatementSyntax> EnclosingStatements = default,
        bool IncludesFirstLabels = false,
        SwitchJoin? SwitchJoin = null,
        SwitchStatementSyntax? OwnerSwitch = null,
        SwitchJoinPlan? SwitchJoinPlan = null,
        Cycle? Cycle = null,
        bool IsLoopBody = false,
        bool IsLatch = false);

    private sealed record SwitchSectionJoinState(
        SwitchStatementSyntax SwitchStatement,
        int Section,
        Region? Below,
        SwitchJoinPlan Plan,
        ImmutableArray<SwitchJoin> SectionJoins,
        int[] Sites);

    // A loop or switch that a break leaves (the regions above Below are inside it) and, for a loop, where a continue
    // goes (the regions above BodyRegion—or Below when the body has no region—are inside it)
    private sealed record JumpTarget(
        SyntaxNode Node,
        bool IsLoop,
        Region? Below,
        Region? BodyRegion,
        string? ContinueLabel,
        Func<GotoStatementSyntax, int>? SectionLookup = null,
        string? JoinLabel = null,
        ImmutableArray<Cycle> Dropped = default);
}
