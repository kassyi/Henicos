using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Lowers flat C# AST statements into a switch-based state machine separated by thread barriers. With
/// <see cref="LiftedConstants"/> and <see cref="MovedLocalFunctions"/>, a body that has split points gets its const
/// declarations and its local functions in front of the <c>switch</c>, where every state sees them.
/// </summary>
/// <param name="isDeviceFunction">
/// Whether the body belongs to a device function. Its end completes the call (a plain <c>return</c>, which the emitters
/// turn into "reset the frame and report completion") instead of finishing the thread.
/// </param>
/// <param name="semanticModel">
/// The semantic model of the method's syntax tree, used to key user labels and <c>goto</c> targets by symbol and region
/// (<see cref="DeclarationKey"/>).
/// </param>
public class StateMachineLowerer(bool isDeviceFunction = false, SemanticModel? semanticModel = null)
{
    // The plan that AstLoweringPipeline gives this pass and ContextHoister, or null to make one from the block
    private readonly StatePlan? _plan;

    /// <summary>Creates a lowerer that takes the labels that start a state and their states from <paramref name="plan"/>.</summary>
    internal StateMachineLowerer(bool isDeviceFunction, SemanticModel? semanticModel, StatePlan plan)
        : this(isDeviceFunction, semanticModel) =>
        _plan = plan;

    /// <summary>
    /// The keys of the local functions that <see cref="Lower"/> writes in front of the <c>switch</c>, in the order
    /// written, instead of in the state of their declaration (<see cref="StateMachineLocalFunctions.Plan"/>). Empty for
    /// a body without a split point; the locals that the functions capture are hoisted
    /// (<see cref="HoistedVariableAnalysis"/>).
    /// </summary>
    internal HashSet<DeclarationKey> MovedLocalFunctions { get; init; } = [];

    /// <summary>
    /// The const declarations of the body that <see cref="Lower"/> writes in front of the <c>switch</c>, in the order
    /// written and before the local functions, which may read them. A const has no run-time state, so one declaration
    /// there keeps it a constant in every state (<see cref="ContextHoister.LiftedConstants"/>). Empty for a body
    /// without a split point.
    /// </summary>
    internal ImmutableArray<LocalDeclarationStatementSyntax> LiftedConstants { get; init; } = [];

    private readonly ImmutableArray<InvocationExpressionSyntax>.Builder _callSites =
        ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

    /// <summary>The calls that may suspend, in frame index order (see <see cref="LoweredStateMachine.CallSites"/>).</summary>
    public IReadOnlyList<InvocationExpressionSyntax> CallSites => _callSites.ToImmutable();

    /// <summary>
    /// The warp collectives of the body, numbered in order after the sites that <see cref="ReserveWarpSites"/> reserved
    /// (see <see cref="LoweredStateMachine.WarpSiteCount"/>).
    /// </summary>
    public int WarpSiteCount { get; private set; }

    /// <summary>
    /// Reserves the first <paramref name="count"/> warp sites of the frame for the branches that
    /// <see cref="ReconvergenceInserter"/> numbered; the warp collectives are numbered after them.
    /// </summary>
    internal void ReserveWarpSites(int count) => WarpSiteCount = count;

    /// <summary>Transforms statements into switch sections keyed by state indices across barrier boundaries.</summary>
    public BlockSyntax Lower(BlockSyntax block)
    {
        // The labels that start a state and their states, numbered by StateLabelPlanner (the only definition of the
        // numbering). Any other label stays a label inside its state.
        var plan = _plan ?? StateLabelPlanner.Plan(block, semanticModel);
        var labelsReachedFromSwitch = FindLabelsReachedFromSwitch(block, plan.LabelStates);
        var sections = new SectionBuilder();
        var functions = ImmutableArray.CreateBuilder<StatementSyntax>();
        functions.AddRange(LiftedConstants);
        foreach (var written in block.Statements)
        {
            var stmt = written;
            if (MovedLocalFunctions.Count > 0 && StateMachineLocalFunctions.GetMoved(stmt) is { } function &&
                MovedLocalFunctions.Contains(LoweringIdentity.ForDeclaration(function, semanticModel)))
            {
                functions.Add(function);
                if (StateMachineLocalFunctions.WithoutFunction(stmt) is not { } remainder)
                {
                    continue;
                }

                stmt = remainder;
            }

            if (stmt is LabeledStatementSyntax labeled && plan.StartsState(labeled))
            {
                LowerStateLabel(labeled, plan.LabelStates, labelsReachedFromSwitch, sections);
            }
            else if (BarrierHelper.GetPhaseBarrierInvocation(stmt) is { } splitPoint)
            {
                LowerSplitPoint(stmt, splitPoint, plan.LabelStates, sections);
            }
            else
            {
                sections.Add(RewriteGoto(stmt, plan.LabelStates));
            }
        }

        if (isDeviceFunction)
        {
            // Falling off the end completes the call
            sections.Add(SyntaxFactory.ReturnStatement());
            sections.CommitSection();
        }
        else
        {
            // Final state: -1
            sections.CommitState(-1, isBarrier: true);
        }

        // switch (__gsharp_state) { case 0: { ... } case 1: { ... } ... }
        functions.Add(sections.ToSwitch());
        return SyntaxFactory.Block(functions);
    }

    private void LowerStateLabel(LabeledStatementSyntax labeled, IReadOnlyDictionary<DeclarationKey, int> labelStates,
        HashSet<DeclarationKey> labelsReachedFromSwitch, SectionBuilder sections)
    {
        // A label boundary requires falling through to the next state case without yielding.
        sections.CommitState(sections.State + 1, isBarrier: false);
        CheckLabelState(labeled, sections.State, labelStates);

        // [WHY] A label that a goto inside a switch of the user reaches stays in its section, and that goto stays a
        // goto: a goto case binds to the innermost switch, which is the user's, not the state machine's. C# finds a
        // label of any section of the enclosing switch, and C++ labels have function scope. The label after a
        // statement of a Gpu.SyncGrid body that returns always stays: the return jumps to it from wherever it is
        // (#626). A continue of a flattened loop is a goto to its continue label (#670).
        if (IsSyncGridNextLabel(labeled.Identifier.ValueText) ||
            labelsReachedFromSwitch.Contains(LoweringIdentity.ForDeclaration(labeled, semanticModel)))
        {
            // <label>: ; (its statement goes into the block of the state below)
            sections.KeepLabel(labeled.WithStatement(SyntaxFactory.EmptyStatement()));
        }

        if (labeled.Statement is not EmptyStatementSyntax)
        {
            sections.Add(RewriteGoto(labeled.Statement, labelStates));
        }
    }

    private enum SplitPointKind
    {
        SuspendingCall,
        Reconvergence,
        WarpShuffle,
        Barrier,
    }

    private static SplitPointKind ClassifySplitPoint(
        InvocationExpressionSyntax splitPoint,
        out WarpShuffleKind shuffleKind)
    {
        shuffleKind = default;
        if (BarrierHelper.IsSuspendingCall(splitPoint))
        {
            return SplitPointKind.SuspendingCall;
        }

        if (BarrierHelper.IsReconvergence(splitPoint))
        {
            return SplitPointKind.Reconvergence;
        }

        if (BarrierHelper.TryGetWarpShuffle(splitPoint, out shuffleKind))
        {
            return SplitPointKind.WarpShuffle;
        }

        return SplitPointKind.Barrier;
    }

    private void LowerSplitPoint(StatementSyntax stmt, InvocationExpressionSyntax splitPoint,
        IReadOnlyDictionary<DeclarationKey, int> labelStates, SectionBuilder sections)
    {
        var kind = ClassifySplitPoint(splitPoint, out var shuffleKind);
        switch (kind)
        {
            case SplitPointKind.SuspendingCall:
                LowerSuspendingCall(stmt, splitPoint, sections);
                break;
            case SplitPointKind.Reconvergence:
                LowerReconvergence(stmt, splitPoint, sections);
                break;
            case SplitPointKind.WarpShuffle:
                LowerWarpShuffle(stmt, splitPoint, shuffleKind, labelStates, sections);
                break;
            case SplitPointKind.Barrier:
                LowerBarrier(splitPoint, sections);
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(SplitPointKind)}: {kind}");
        }
    }

    // The call gets a state of its own: every resume runs it again until the callee completes
    private void LowerSuspendingCall(StatementSyntax stmt, InvocationExpressionSyntax call, SectionBuilder sections)
    {
        sections.CommitState(sections.State + 1, isBarrier: false);
        sections.Add(CreateResumeStatement(stmt, call, sections.State));
    }

    // [WHY] The lanes of the mask meet at the same instance of the call (#1468): each lane publishes its value with the
    // site, its path through the device function frames and how often it reached the site, then stops; the launcher runs
    // it again once every lane of the mask that has not exited has published the same instance, and the read takes the
    // values of that instance only
    private void LowerWarpShuffle(StatementSyntax stmt, InvocationExpressionSyntax shuffle, WarpShuffleKind shuffleKind,
        IReadOnlyDictionary<DeclarationKey, int> labelStates, SectionBuilder sections)
    {
        var shape = WarpShuffleShape.ForKind(shuffleKind);
        var site = WarpSiteCount++;
        var place = SourcePlace.Format(semanticModel, shuffle);

        // 1. Publish the value of the lane for this instance of the call
        sections.Add(WarpShuffleSyntax.CreatePublish(shuffle, shape, site, place));

        // 2. State Split & Return
        sections.CommitState(sections.State + 1, isBarrier: true);

        // 3. Rewrite original Warp.ShuffleXXX(...) to __gsharp_warp_shuffle_read_XXX(...)
        var readStmt = WarpShuffleSyntax.RewriteRead(stmt, shuffle, shape, site, place);
        sections.Add(RewriteGoto(readStmt, labelStates));
    }

    // [WHY] The lanes that entered a branch or loop of a method that reaches Warp.ActiveMask() wait for each other where
    // it ends (#844), as the GPU runs them together again there: a lane arrives and stops, and the launcher runs it again
    // once no lane of the warp is still inside the same entry of the branch
    // __gsharp_warp_reconverge_arrive(...); __gsharp_state = <n>; return; case <n>: __gsharp_warp_reconverge_pass(...);
    private static void LowerReconvergence(StatementSyntax stmt, InvocationExpressionSyntax arrive,
        SectionBuilder sections)
    {
        sections.Add(stmt);
        sections.CommitState(sections.State + 1, isBarrier: true);
        var arguments = arrive.ArgumentList.Arguments;
        sections.Add(
            SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(GSharpInternalNames.Prelude.WarpReconvergePass),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SeparatedList<ArgumentSyntax>([arguments[0], arguments[1], arguments[2]])
                    )
                )
            ).WithOriginalSpanFrom(stmt)
        );
    }

    private static void LowerBarrier(InvocationExpressionSyntax barrier, SectionBuilder sections)
    {
        // The launcher keeps a thread that stopped at a barrier waiting there (#664): at a grid sync until the next grid
        // phase, at a block barrier until every thread of the block that has not finished waits too. A shuffle is not a
        // wait: the thread goes on in the next round
        if (BarrierHelper.IsGridBarrier(barrier))
        {
            sections.Add(SetFlag(GSharpInternalNames.StateMachine.YieldedForGridSync));
        }
        else if (BarrierHelper.IsBlockBarrier(barrier))
        {
            sections.Add(SetFlag(GSharpInternalNames.StateMachine.YieldedForBlockBarrier));
        }

        sections.CommitState(sections.State + 1, isBarrier: true);
    }

    // The switch sections built so far, the statements of the section being built and the state it has
    private sealed class SectionBuilder
    {
        private readonly ImmutableArray<SwitchSectionSyntax>.Builder _cases =
            ImmutableArray.CreateBuilder<SwitchSectionSyntax>();

        private readonly ImmutableArray<StatementSyntax>.Builder _statements =
            ImmutableArray.CreateBuilder<StatementSyntax>();

        private LabeledStatementSyntax? _sectionLabel;

        /// <summary>The state of the section being built.</summary>
        public int State { get; private set; }

        public void Add(StatementSyntax statement) => _statements.Add(statement);

        /// <summary>Puts <paramref name="label"/> in front of the block of the section being built.</summary>
        public void KeepLabel(LabeledStatementSyntax label) => _sectionLabel = label;

        /// <summary>Ends the section with a jump to <paramref name="nextState"/> and starts the section of that state.</summary>
        public void CommitState(int nextState, bool isBarrier)
        {
            if (isBarrier)
            {
                // __gsharp_state = <next state>; return;
                _statements.Add(CreateStateAssignment(nextState));
                _statements.Add(CreateSuspendingReturn());
            }
            else
            {
                // Unconditional jump to next state within the switch
                _statements.Add(SyntaxFactory.ParseStatement($"goto case {nextState};")
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
            }

            CommitSection();
            State = nextState;
        }

        /// <summary>Adds the section being built to the switch.</summary>
        public void CommitSection()
        {
            // case <state>: { <statements> }
            var label = SyntaxFactory.CaseSwitchLabel(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(State))
            );
            var block = SyntaxFactory.Block(_statements);
            // case <state>: [<label>: ;] { <statements> }
            var statements = _sectionLabel != null
                ? SyntaxFactory.List<StatementSyntax>([_sectionLabel, block])
                : SyntaxFactory.SingletonList<StatementSyntax>(block);
            _cases.Add(
                SyntaxFactory.SwitchSection(
                    SyntaxFactory.SingletonList<SwitchLabelSyntax>(label),
                    statements
                )
            );

            _statements.Clear();
            _sectionLabel = null;
        }

        // switch (__gsharp_state) { <sections> }
        public SwitchStatementSyntax ToSwitch() =>
            SyntaxFactory.SwitchStatement(
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State),
                SyntaxFactory.List(_cases)
            );
    }

    // Emits: if (!__gsharp_resume(<call>, <frame index>[, <result target>])) { __gsharp_state = <resume state>; return; }
    private StatementSyntax CreateResumeStatement(StatementSyntax statement, InvocationExpressionSyntax call,
        int resumeState)
    {
        var resultTarget = statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax } => null,
            ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax { Left: var left } assignment
            } when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) => left,
            _ => throw new InvalidOperationException(
                "A call that may suspend must be lowered to a statement or a simple assignment before state machine lowering.")
        };

        var frameIndex = _callSites.Count;
        _callSites.Add(call);

        var frameIndexLiteral =
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(frameIndex));
        var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>()
            .Add(SyntaxFactory.Argument(call))
            .Add(SyntaxFactory.Argument(frameIndexLiteral));
        if (resultTarget != null)
        {
            arguments = arguments.Add(SyntaxFactory.Argument(resultTarget));
        }

        // __gsharp_resume(<call>, <frame index>[, <result target>])
        var resume = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.ResumeCall),
            SyntaxFactory.ArgumentList(arguments)
        );

        return SyntaxFactory.IfStatement(
            SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, resume),
            SyntaxFactory.Block(
                CreateStateAssignment(resumeState),
                CreateSuspendingReturn()
            )
        );
    }

    // return; marked so that the emitters treat it as a suspension, not as a return of the user's code
    private static ReturnStatementSyntax CreateSuspendingReturn() =>
        SyntaxFactory.ReturnStatement().WithAdditionalAnnotations(
            new SyntaxAnnotation(LoweringAnnotations.StateMachineReturnAnnotationKind)
        );

    // [WHY] The plan numbers the states on the body before ContextHoister; this pass splits them on the body after it.
    // ContextHoister moves no label or split point, so the numbers agree. A pass that broke that would make a goto case
    // jump to another state than its label's, which neither the C# nor the C++ compiler would catch.
    private void CheckLabelState(LabeledStatementSyntax labeled, int state,
        IReadOnlyDictionary<DeclarationKey, int> labelStates)
    {
        if (!labelStates.TryGetValue(LoweringIdentity.ForDeclaration(labeled, semanticModel), out var planned) ||
            planned != state)
        {
            throw new InvalidOperationException(
                $"The label '{labeled.Identifier.ValueText}' starts state {state}, but the state plan numbered it differently.");
        }
    }

    // The label a plain goto names, when the goto is inside a switch of the user: such a goto stays a goto, and its
    // label stays at the start of its state (see LowerStateLabel). The state machine's own switch does not exist yet,
    // so every enclosing switch is the user's. FindLabelsReachedFromSwitch and GotoRewriter both decide by this test
    private static IdentifierNameSyntax? GetLabelJumpedToFromSwitch(GotoStatementSyntax jump) =>
        jump.IsKind(SyntaxKind.GotoStatement) &&
        jump.Expression is IdentifierNameSyntax target &&
        jump.Ancestors()
            .AsStackEnumerable()
            .Any(static node => node is SwitchStatementSyntax)
            ? target
            : null;

    // The labels that start a state and that a goto inside a switch of the user reaches (see LowerStateLabel)
    private HashSet<DeclarationKey> FindLabelsReachedFromSwitch(BlockSyntax block,
        IReadOnlyDictionary<DeclarationKey, int> labelStates)
    {
        var labels = new HashSet<DeclarationKey>();
        block.DescendantNodes()
            .AsStackEnumerable()
            .OfType<GotoStatementSyntax>()
            .Select(GetLabelJumpedToFromSwitch)
            .OfType<IdentifierNameSyntax>()
            .Select(semanticModel, static (target, model) => LoweringIdentity.ForReference(target, model))
            .Where(labelStates, static (key, states) => states.ContainsKey(key))
            .AddTo(labels);
        return labels;
    }

    private StatementSyntax RewriteGoto(StatementSyntax stmt, IReadOnlyDictionary<DeclarationKey, int> labelStates) =>
        (StatementSyntax)new GotoRewriter(labelStates, semanticModel).Visit(stmt);

    private class GotoRewriter(IReadOnlyDictionary<DeclarationKey, int> labelStates, SemanticModel? semanticModel)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
        {
            // A goto to the label after a returning statement of a Gpu.SyncGrid body, and a goto inside a switch of the
            // user, stay gotos to the label that their section keeps (see LowerStateLabel)
            if (GetLabelJumpedToFromSwitch(node) is not null ||
                !node.IsKind(SyntaxKind.GotoStatement) ||
                node.Expression is not IdentifierNameSyntax id ||
                IsSyncGridNextLabel(id.Identifier.ValueText))
            {
                return base.VisitGotoStatement(node);
            }

            if (labelStates.TryGetValue(LoweringIdentity.ForReference(id, semanticModel), out var targetState))
            {
                return SyntaxFactory.ParseStatement($"goto case {targetState};")
                    .WithTriviaFrom(node);
            }

            return base.VisitGotoStatement(node);
        }
    }

    private static bool IsSyncGridNextLabel(string name) =>
        name.StartsWith(GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal);

    // __gsharp_state = <stateValue>;
    private static StatementSyntax CreateStateAssignment(int stateValue)
    {
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State),
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(stateValue))
            )
        );
    }

    // <flag> = true;
    private static ExpressionStatementSyntax SetFlag(string flag) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(flag),
                SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
            )
        );
}
