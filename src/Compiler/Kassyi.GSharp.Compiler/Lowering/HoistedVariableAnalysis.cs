namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Decides which local declarations of a body move into the context struct (<see cref="ContextHoister"/>): those that
/// cross a phase barrier, those used after a label that starts another state, a <c>SharedMemory&lt;T&gt;</c>, and the
/// result of a call that may suspend.
/// </summary>
internal static class HoistedVariableAnalysis
{
    /// <summary>
    /// Adds the key of every hoisted declaration of <paramref name="block"/> to <paramref name="hoistedKeys"/> and the
    /// declarators themselves, in the order written, to <paramref name="hoistedDeclarators"/>.
    /// </summary>
    /// <returns>Whether the block has a split point; without one nothing is hoisted.</returns>
    public static bool Analyze(
        BlockSyntax block,
        SemanticModel? semanticModel,
        StatePlan? plan,
        HashSet<DeclarationKey> hoistedKeys,
        ImmutableArray<VariableDeclaratorSyntax>.Builder hoistedDeclarators)
    {
        // [WHY] One-pass AST traversal to collect barriers and variables efficiently in O(N).
        var analyzer = new HoistAnalyzer();
        analyzer.Visit(block);
        if (analyzer.BarrierSpans.Count == 0)
        {
            return false;
        }

        var boundaries = FindStateBoundaries(block, analyzer.BarrierSpans, semanticModel, plan);
        var variableCount = analyzer.Variables.Count;
        var keys = new DeclarationKey[variableCount];
        for (var v = 0; v < variableCount; v++)
        {
            var (decl, scopeEnd) = analyzer.Variables[v];
            keys[v] = LoweringIdentity.ForDeclaration(decl, semanticModel);
            if (MustHoist(decl, scopeEnd, keys[v], boundaries))
            {
                hoistedKeys.Add(keys[v]);
            }
        }

        // [WHY] Each declaration that crosses a barrier is hoisted on its own (DeclarationKey: symbol + region), so a
        // sibling local of the same name that does not cross one stays a local. Without a semantic model the key is the
        // name, and every declaration of a hoisted name moves into the context, which keeps the name-keyed rewrite
        // consistent.
        for (var v = 0; v < variableCount; v++)
        {
            if (hoistedKeys.Contains(keys[v]))
            {
                hoistedDeclarators.Add(analyzer.Variables[v].Decl);
            }
        }

        return true;
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
    // otherwise when a barrier lies in its scope after it, or it is used after a split label in its scope
    private static bool MustHoist(
        VariableDeclaratorSyntax decl,
        int scopeEnd,
        DeclarationKey key,
        StateBoundaries boundaries)
    {
        if (IsAlwaysHoisted(decl))
        {
            return true;
        }

        var declEnd = decl.Span.End;
        return CrossesBarrier(boundaries.BarrierStarts, declEnd, scopeEnd) ||
               (boundaries.LastReferenceStarts is { } lastReferenceStarts &&
                IsUsedAfterSplitLabel(boundaries.SplitLabelStarts, lastReferenceStarts, key, declEnd, scopeEnd));
    }

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
            GotoStatementSyntax => true,
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
        public readonly ImmutableArray<TextSpan>.Builder BarrierSpans = ImmutableArray.CreateBuilder<TextSpan>();

        public readonly ImmutableArray<(VariableDeclaratorSyntax Decl, int ScopeEnd)>.Builder Variables =
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
