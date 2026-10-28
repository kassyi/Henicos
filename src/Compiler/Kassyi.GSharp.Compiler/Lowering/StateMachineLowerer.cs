namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Lowers flat C# AST statements into a switch-based state machine separated by thread barriers.</summary>
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

    private readonly ImmutableArray<InvocationExpressionSyntax>.Builder _callSites =
        ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

    /// <summary>The calls that may suspend, in frame index order (see <see cref="LoweredStateMachine.CallSites"/>).</summary>
    public IReadOnlyList<InvocationExpressionSyntax> CallSites => _callSites.ToImmutable();

    /// <summary>Transforms statements into switch sections keyed by state indices across barrier boundaries.</summary>
    public BlockSyntax Lower(BlockSyntax block)
    {
        // The labels that start a state and their states, numbered by StateLabelPlanner (the only definition of the
        // numbering). Any other label stays a label inside its state.
        var plan = _plan ?? StateLabelPlanner.Plan(block, semanticModel);
        var sections = new SectionBuilder();
        foreach (var stmt in block.Statements)
        {
            if (stmt is LabeledStatementSyntax labeled && plan.StartsState(labeled))
            {
                LowerStateLabel(labeled, plan.LabelStates, sections);
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
        return SyntaxFactory.Block(sections.ToSwitch());
    }

    private void LowerStateLabel(LabeledStatementSyntax labeled, IReadOnlyDictionary<DeclarationKey, int> labelStates,
        SectionBuilder sections)
    {
        // A label boundary requires falling through to the next state case without yielding.
        sections.CommitState(sections.State + 1, isBarrier: false);
        CheckLabelState(labeled, sections.State, labelStates);

        // [WHY] The label after a statement of a Gpu.SyncGrid body that returns stays in its section: the return jumps
        // to it with goto from wherever it is, also from a switch of the user, where a goto case would bind to that
        // switch (#626)
        if (IsSyncGridNextLabel(labeled.Identifier.ValueText))
        {
            sections.KeepLabel(labeled);
        }

        if (labeled.Statement is not EmptyStatementSyntax)
        {
            sections.Add(RewriteGoto(labeled.Statement, labelStates));
        }
    }

    private void LowerSplitPoint(StatementSyntax stmt, InvocationExpressionSyntax splitPoint,
        IReadOnlyDictionary<DeclarationKey, int> labelStates, SectionBuilder sections)
    {
        if (BarrierHelper.IsSuspendingCall(splitPoint))
        {
            LowerSuspendingCall(stmt, splitPoint, sections);
        }
        else if (BarrierHelper.TryGetWarpShuffleRead(splitPoint, out var readFunction))
        {
            LowerWarpShuffle(stmt, splitPoint, readFunction, labelStates, sections);
        }
        else
        {
            LowerBarrier(splitPoint, sections);
        }
    }

    // The call gets a state of its own: every resume runs it again until the callee completes
    private void LowerSuspendingCall(StatementSyntax stmt, InvocationExpressionSyntax call, SectionBuilder sections)
    {
        sections.CommitState(sections.State + 1, isBarrier: false);
        sections.Add(CreateResumeStatement(stmt, call, sections.State));
    }

    private void LowerWarpShuffle(StatementSyntax stmt, InvocationExpressionSyntax shuffle, string readFunction,
        IReadOnlyDictionary<DeclarationKey, int> labelStates, SectionBuilder sections)
    {
        // 1. Publish val to the context's warp exchange slot
        sections.Add(CreateWarpExchangeAssign(GetShuffleArgument(shuffle, ShuffleValuePosition).Expression));

        // 2. State Split & Return
        sections.CommitState(sections.State + 1, isBarrier: true);

        // 3. Rewrite original Warp.ShuffleXXX(...) to __gsharp_warp_shuffle_read_XXX(...)
        var readStmt = RewriteWarpShuffleRead(stmt, shuffle, readFunction);
        sections.Add(RewriteGoto(readStmt, labelStates));
    }

    private static void LowerBarrier(InvocationExpressionSyntax barrier, SectionBuilder sections)
    {
        if (BarrierHelper.IsGridBarrier(barrier))
        {
            // __gsharp_yielded_for_grid_sync = true;
            sections.Add(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.YieldedForGridSync),
                        SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                    )
                )
            );
        }

        sections.CommitState(sections.State + 1, isBarrier: true);
    }

    // __gsharp_warp_exchange_assign(<val>);
    private static ExpressionStatementSyntax CreateWarpExchangeAssign(ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(GSharpInternalNames.Prelude.WarpExchangeAssign),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value)))
            )
        );

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

    private StatementSyntax RewriteGoto(StatementSyntax stmt, IReadOnlyDictionary<DeclarationKey, int> labelStates) =>
        (StatementSyntax)new GotoRewriter(labelStates, semanticModel).Visit(stmt);

    private class GotoRewriter(IReadOnlyDictionary<DeclarationKey, int> labelStates, SemanticModel? semanticModel)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
        {
            // A goto to the label after a returning statement of a Gpu.SyncGrid body stays a goto (see Lower)
            if (!node.IsKind(SyntaxKind.GotoStatement) ||
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

    private StatementSyntax RewriteWarpShuffleRead(StatementSyntax stmt, InvocationExpressionSyntax barrierInv,
        string readFunction)
    {
        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>();
        // The root context owns the warp exchange, also when the shuffle is in a device function
        newArgs = newArgs.Add(
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Root)));
        newArgs = newArgs.Add(GetShuffleArgument(barrierInv, ShuffleMaskPosition));
        newArgs = newArgs.Add(GetShuffleArgument(barrierInv, ShuffleValuePosition)); // pass val for type inference
        newArgs = newArgs.Add(GetShuffleArgument(barrierInv, ShuffleLanePosition));
        // The width is always passed, so that the place of the shuffle can follow it; 32 is the default of every shuffle
        newArgs = newArgs.Add(
            TryGetShuffleArgument(barrierInv, ShuffleWidthPosition, out var width)
                ? width
                : SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(32))
                )
        );
        newArgs = newArgs.Add(
            SyntaxFactory.Argument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(SourcePlace.Format(semanticModel, barrierInv))
                )
            )
        );

        // __gsharp_warp_shuffle_read_<kind>(__gsharp_root, mask, val, delta, width, "<file>(<line>,<column>)")
        var rewrittenInv = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(readFunction),
            SyntaxFactory.ArgumentList(newArgs)
        );

        return stmt.ReplaceNode(barrierInv, rewrittenInv);
    }


    // The parameters of every Warp shuffle, in order: mask, var, the lane operand (srcLane, delta or laneMask), width
    private const int ShuffleMaskPosition = 0;
    private const int ShuffleValuePosition = 1;
    private const int ShuffleLanePosition = 2;
    private const int ShuffleWidthPosition = 3;
    private const string ShuffleMaskParameter = "mask";
    private const string ShuffleValueParameter = "var";
    private const string ShuffleWidthParameter = "width";

    // The argument of a shuffle for the parameter at position, which the call compiled with (so it is there)
    private static ArgumentSyntax GetShuffleArgument(InvocationExpressionSyntax shuffle, int position) =>
        TryGetShuffleArgument(shuffle, position, out var argument)
            ? argument
            : throw new InvalidOperationException($"A warp shuffle without the argument at position {position}");

    // The argument of a shuffle for the parameter at position, without its name: a named argument may stand anywhere
    // (Warp.ShuffleSync(m, v, width: 8, srcLane: 3)), and the read functions take the arguments by position
    private static bool TryGetShuffleArgument(
        InvocationExpressionSyntax shuffle,
        int position,
        [NotNullWhen(true)] out ArgumentSyntax? argument)
    {
        var arguments = shuffle.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            var target = arguments[i].NameColon?.Name.Identifier.ValueText switch
            {
                null => i,
                ShuffleMaskParameter => ShuffleMaskPosition,
                ShuffleValueParameter => ShuffleValuePosition,
                ShuffleWidthParameter => ShuffleWidthPosition,
                _ => ShuffleLanePosition
            };
            if (target == position)
            {
                argument = arguments[i].WithNameColon(null);
                return true;
            }
        }

        argument = null;
        return false;
    }
}
