namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Decides which local declarations of a body move into the context struct (<see cref="ContextHoister"/>): those that
/// cross a phase barrier, those used after a label that starts another state, a <c>SharedMemory&lt;T&gt;</c>, and the
/// result of a call that may suspend. A local that a local function written in front of the <c>switch</c> captures
/// (<see cref="StateMachineLocalFunctions"/>) moves too, as no state declares it.
/// </summary>
internal static class HoistedVariableAnalysis
{
    /// <summary>
    /// Finds the hoisted declarations of <paramref name="block"/> and the const declarators that go in front of its
    /// state switch instead (<see cref="HoistAnalysis"/>).
    /// </summary>
    /// <param name="block">The body to analyze.</param>
    /// <param name="semanticModel">The semantic model of the method's syntax tree, or <see langword="null"/>.</param>
    /// <param name="plan">The labels that start a state, or <see langword="null"/> to plan them from the block.</param>
    /// <param name="capturedByMovedFunctions">
    /// The keys that the local functions written in front of the <c>switch</c> reference
    /// (<see cref="MovedLocalFunctions.Captured"/>), or <see langword="null"/> when none moves.
    /// </param>
    /// <param name="refLocalsNotLiveAcrossSplit">
    /// The <c>ref</c> locals that are not live after any split point (<see cref="RefLocalLiveness"/>): a barrier in
    /// their scope does not hoist them.
    /// </param>
    /// <returns>The hoisted declarations and the lifted consts; without a split point, nothing.</returns>
    public static HoistAnalysis Analyze(
        BlockSyntax block,
        SemanticModel? semanticModel,
        StatePlan? plan,
        HashSet<DeclarationKey>? capturedByMovedFunctions,
        HashSet<ISymbol>? refLocalsNotLiveAcrossSplit = null)
    {
        // [WHY] One-pass AST traversal to collect barriers and variables efficiently in O(N).
        var analyzer = new HoistAnalyzer();
        analyzer.Visit(block);
        if (analyzer.BarrierSpans.Count == 0)
        {
            return HoistAnalysis.WithoutSplitPoint;
        }

        var hoistedKeys = new HashSet<DeclarationKey>();
        var liftedConstants = new HashSet<DeclarationKey>();

        var boundaries = FindStateBoundaries(block, analyzer.BarrierSpans, semanticModel, plan);
        var variableCount = analyzer.Variables.Count;
        var keys = new DeclarationKey[variableCount];
        for (var v = 0; v < variableCount; v++)
        {
            var (decl, scopeEnd) = analyzer.Variables[v];
            keys[v] = LoweringIdentity.ForDeclaration(decl, semanticModel);
            // [WHY] A const has no run-time state: StateMachineLowerer declares every const of a split body in front of
            // its switch, where every state sees it, instead of a context field, which is not a constant (#955). All of
            // them go there, so that a lifted const initializer always finds the consts it reads
            if (IsConstDeclarator(decl))
            {
                if (IsBodyLocal(decl))
                {
                    liftedConstants.Add(keys[v]);
                }

                continue;
            }

            if (MustHoist(decl, scopeEnd, keys[v], boundaries, refLocalsNotLiveAcrossSplit) ||
                (capturedByMovedFunctions?.Contains(keys[v]) == true && IsBodyLocal(decl)))
            {
                hoistedKeys.Add(keys[v]);
            }
        }

        // [WHY] Each declaration that crosses a barrier is hoisted on its own (DeclarationKey: symbol + region), so a
        // sibling local of the same name that does not cross one stays a local. Without a semantic model the key is the
        // name, and every declaration of a hoisted name moves into the context, which keeps the name-keyed rewrite
        // consistent.
        var hoistedDeclarators = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        for (var v = 0; v < variableCount; v++)
        {
            if (hoistedKeys.Contains(keys[v]))
            {
                hoistedDeclarators.Add(analyzer.Variables[v].Decl);
            }
        }

        return new HoistAnalysis(true, hoistedDeclarators.ToImmutable(), hoistedKeys, liftedConstants);
    }

    // The starts of the barriers and of the labels that start a state, and the last references when there are labels
    private static StateBoundaries FindStateBoundaries(
        BlockSyntax block,
        ImmutableArray<TextSpan>.Builder barrierSpans,
        SemanticModel? semanticModel,
        StatePlan? plan)
    {
        var barrierStarts = new int[barrierSpans.Count];
        for (var i = 0; i < barrierStarts.Length; i++)
        {
            barrierStarts[i] = barrierSpans[i].Start;
        }

        var splitLabelStarts = FindSplitLabelStarts(block, semanticModel, plan);
        var lastReferenceStarts = splitLabelStarts.Length > 0
            ? FindLastReferenceStarts(block, semanticModel)
            : null;
        return new StateBoundaries(barrierStarts, splitLabelStarts, lastReferenceStarts);
    }

    // Whether the declaration lives in the context: always for shared memory and the result of a suspending call;
    // otherwise when a barrier lies in its scope after it (unless it is a ref local that no state after a split reads),
    // or it is used after a split label in its scope. The caller adds the locals that a local function written in front
    // of the switch captures
    private static bool MustHoist(
        VariableDeclaratorSyntax decl,
        int scopeEnd,
        DeclarationKey key,
        StateBoundaries boundaries,
        HashSet<ISymbol>? refLocalsNotLiveAcrossSplit)
    {
        if (IsAlwaysHoisted(decl))
        {
            return true;
        }

        // [WHY] A ref local cannot be a field of the managed frame class (CS9059). One that is not live after any split
        // point is declared and used within one state, so it stays a local there (#1640); a live one is GSH0039
        var staysLocal = key.Symbol != null && refLocalsNotLiveAcrossSplit != null &&
                         refLocalsNotLiveAcrossSplit.Contains(key.Symbol);
        var declEnd = decl.Span.End;
        return (!staysLocal && CrossesBarrier(boundaries.BarrierStarts, declEnd, scopeEnd)) ||
               (boundaries.LastReferenceStarts is { } lastReferenceStarts &&
                IsUsedAfterSplitLabel(boundaries.SplitLabelStarts, lastReferenceStarts, key, declEnd, scopeEnd));
    }

    // Decided by the syntax alone, so that a lowering without a semantic model lifts the same declarations
    private static bool IsConstDeclarator(VariableDeclaratorSyntax decl) =>
        decl.Parent?.Parent is LocalDeclarationStatementSyntax { IsConst: true };

    // Whether the declaration belongs to the body itself: a local of a local function or a lambda stays in it
    private static bool IsBodyLocal(VariableDeclaratorSyntax decl) =>
        !decl.Ancestors()
            .AsStackEnumerable()
            .Any(static ancestor => ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

    private static bool IsAlwaysHoisted(VariableDeclaratorSyntax decl)
    {
        var isSharedMemory = decl.Parent is VariableDeclarationSyntax
        {
            Type: GenericNameSyntax { Identifier.ValueText: TypeConstants.SharedMemoryType }
        };

        // The result of a call that may suspend is written when the callee completes, after any number of resumes
        var isSuspendingCallResult = decl.Initializer?.Value is InvocationExpressionSyntax initializerCall &&
                                     BarrierHelper.IsSuspendingCall(initializerCall);
        return isSharedMemory || isSuspendingCallResult;
    }

    // [WHY] Binary search (O(V log B)) is used to determine if variables cross any synchronization barriers: the first
    // barrier after the declaration crosses when it comes before the end of the scope.
    private static bool CrossesBarrier(int[] barrierStarts, int declEnd, int scopeEnd)
    {
        var index = Array.BinarySearch(barrierStarts, declEnd);
        if (index < 0)
        {
            index = ~index;
        }

        return index < barrierStarts.Length && barrierStarts[index] < scopeEnd;
    }

    // [WHY] A label that starts a state ends the case block of its declaration, as a barrier does. Only a local that is
    // used after such a label moves into the context (as pocl saves only the values used outside their region); the
    // others stay locals. Lowering's own labels count too: a flattened switch starts a state at every section, and C#
    // lets a section use a local that an earlier section declares after its last barrier (#708, #795).
    private static int[] FindSplitLabelStarts(BlockSyntax block, SemanticModel? semanticModel, StatePlan? plan)
    {
        var statePlan = plan ?? StateLabelPlanner.Plan(block, semanticModel);
        var starts = ImmutableArray.CreateBuilder<int>();
        foreach (var statement in block.Statements)
        {
            if (statement is LabeledStatementSyntax labeled && statePlan.StartsState(labeled))
            {
                starts.Add(labeled.SpanStart);
            }
        }

        return starts.ToArray();
    }

    // The start of the last reference to each declaration, in a value position
    private static Dictionary<DeclarationKey, int> FindLastReferenceStarts(
        BlockSyntax block,
        SemanticModel? semanticModel)
    {
        var lastStarts = new Dictionary<DeclarationKey, int>();
        foreach (var node in block.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier || IsNonValueIdentifierPosition(identifier))
            {
                continue;
            }

            var key = LoweringIdentity.ForReference(identifier, semanticModel);
            if (!lastStarts.TryGetValue(key, out var last) || identifier.SpanStart > last)
            {
                lastStarts[key] = identifier.SpanStart;
            }
        }

        return lastStarts;
    }

    // Whether a split label of the user lies between the declaration and the end of its scope, and the declaration is
    // referenced after the first such label
    private static bool IsUsedAfterSplitLabel(
        int[] splitLabelStarts,
        Dictionary<DeclarationKey, int> lastReferenceStarts,
        DeclarationKey key,
        int declEnd,
        int scopeEnd)
    {
        var index = Array.BinarySearch(splitLabelStarts, declEnd);
        if (index < 0)
        {
            index = ~index;
        }

        return index < splitLabelStarts.Length &&
               splitLabelStarts[index] < scopeEnd &&
               lastReferenceStarts.TryGetValue(key, out var lastReference) &&
               lastReference > splitLabelStarts[index];
    }

    /// <summary>
    /// Whether <paramref name="node"/> names a member, type, label, or named argument/property rather than reading or
    /// writing a local variable.
    /// </summary>
    /// <remarks>
    /// Such positions must never be rewritten, even when lowering runs without a SemanticModel (Tier 2).
    /// </remarks>
    public static bool IsNonValueIdentifierPosition(IdentifierNameSyntax node) =>
        node.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == node,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name == node,
            QualifiedNameSyntax => true,
            NameColonSyntax => true,
            NameEqualsSyntax => true,
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment => assignment.Left == node,
            GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoStatement } => true,
            _ => false
        };

    // The sorted starts of the barriers and of the labels that start a state, and the start of the last value
    // reference to each declaration (null when no label starts a state)
    private readonly record struct StateBoundaries(
        int[] BarrierStarts,
        int[] SplitLabelStarts,
        Dictionary<DeclarationKey, int>? LastReferenceStarts);

    private sealed class HoistAnalyzer : CSharpSyntaxWalker
    {
        public ImmutableArray<TextSpan>.Builder BarrierSpans { get; } = ImmutableArray.CreateBuilder<TextSpan>();

        public ImmutableArray<(VariableDeclaratorSyntax Decl, int ScopeEnd)>.Builder Variables { get; } =
            ImmutableArray.CreateBuilder<(VariableDeclaratorSyntax, int)>();

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (BarrierHelper.IsPhaseBarrier(node))
            {
                BarrierSpans.Add(node.Span);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            // The resource of a using statement lives until the end of the statement. The analyzer rejects a barrier
            // inside it (KernelBarrierValidator), so it never crosses one; a barrier after it leaves it a plain local.
            if (node.Parent?.Parent is UsingStatementSyntax usingStatement)
            {
                Variables.Add((node, usingStatement.Span.End));
            }
            else if (node.FirstAncestorOrSelf<BlockSyntax>() is { } parentBlock)
            {
                Variables.Add((node, parentBlock.Span.End));
            }

            base.VisitVariableDeclarator(node);
        }
    }
}
