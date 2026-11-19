namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Decides which top-level labels of a flattened body start a state of the CPU state machine.</summary>
/// <remarks>
/// [WHY] A state machine state is a <c>case</c> block of a <c>switch</c>, and C# / C++ cannot jump into another block with
/// <c>goto</c>, so a label that a <c>goto</c> in another state reaches has to start a state of its own (the
/// <c>goto</c> becomes <c>goto case</c>, or, inside a <c>switch</c> of the user, a <c>goto</c> to the label that
/// <see cref="StateMachineLowerer"/> keeps at the start of that state). Any other label stays a C# / C++ label inside
/// its state, so the locals declared before it stay in scope after it, as in Roslyn's async lowering, where only the
/// suspension points start states.
/// <list type="bullet">
///   <item><description>A label that lowering adds (<c>__gsharp_</c>) always starts a state.</description></item>
///   <item><description>A label of the user starts one when a <c>goto</c> to it is in another state.</description></item>
/// </list>
/// A goto to an inner label of chained labels from another state cannot be lowered; the plan lists it (GSH0121) instead
/// of starting a state there.
/// Two labels of one name (in sibling scopes that lowering flattened) do not start states for that reason: the states
/// must split only where a <c>goto</c> needs it, since every other state boundary is a split point (barrier, shuffle,
/// suspending call) that the analyses of the lowered body rely on. The C++ output names labels by symbol, and the
/// managed output renames them (<c>ContextLocalRewriter</c>), so they do not collide. Without a semantic model the
/// labels are keyed by name, two labels of one name share a key, and both start a state as before, so a Tier 2 test of
/// labels of one name uses a semantic model (the <c>*FromTestCase</c> helpers).
/// The states are the stretches between the statements that start a state: the labels found so far and the split
/// points. A new state label can move another <c>goto</c> into another state than its label, so the search repeats until
/// nothing changes; it ends because the set only grows (at most one round per label, plus one).
/// <para>
/// The type is internal: <see cref="AstLoweringPipeline"/> gives its <see cref="StatePlan"/> to the internal
/// constructors of <see cref="ContextHoister"/> and <see cref="StateMachineLowerer"/>; their public constructors (the
/// Tier 2 tests) make the plan themselves.
/// </para>
/// </remarks>
internal static class StateLabelPlanner
{
    /// <summary>Finds the labels of <paramref name="body"/> that start a state and numbers the states they start.</summary>
    /// <param name="body">The flattened body that <see cref="StateMachineLowerer"/> lowers.</param>
    /// <param name="model">The semantic model that keys the labels by symbol, or <see langword="null"/> to key them by name.</param>
    public static StatePlan Plan(BlockSyntax body, SemanticModel? model)
    {
        var stateLabels = FindStateLabels(body, model);
        return new StatePlan(stateLabels, NumberLabelStates(body.Statements, stateLabels, model), model)
        {
            GotosToChainedLabelsAcrossStates = FindGotosToChainedLabelsAcrossStates(body.Statements, stateLabels, model)
        };
    }

    // The keys (LoweringIdentity.ForDeclaration) of the top-level labels of body that start a state
    private static HashSet<DeclarationKey> FindStateLabels(BlockSyntax body, SemanticModel? model)
    {
        var stateLabels = new HashSet<DeclarationKey>();
        var statements = body.Statements;
        var labels = new Dictionary<DeclarationKey, int>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LabeledStatementSyntax labeled)
            {
                labels[LoweringIdentity.ForDeclaration(labeled, model)] = i;
            }
        }

        if (labels.Count == 0)
        {
            return stateLabels;
        }

        var labelCounts = model == null ? LoweringIdentity.CountLabelNames(body, includeLoweringLabels: true) : null;
        foreach (var statement in statements)
        {
            if (statement is LabeledStatementSyntax labeled &&
                (LoweringIdentity.IsLoweringLabel(labeled) || labelCounts?[labeled.Identifier.ValueText] > 1))
            {
                stateLabels.Add(LoweringIdentity.ForDeclaration(labeled, model));
            }
        }

        // The gotos of each top-level statement that reach a top-level label: (statement index, goto, label key)
        var gotos = FindGotos(statements, labels, model);

        var stateOfStatement = new int[statements.Count];
        var changed = gotos.Length > 0;
        while (changed)
        {
            NumberStates(statements, stateLabels, model, stateOfStatement);
            changed = false;
            foreach (var (statement, _, target) in gotos)
            {
                if (!stateLabels.Contains(target) &&
                    stateOfStatement[statement] != stateOfStatement[labels[target]])
                {
                    stateLabels.Add(target);
                    changed = true;
                }
            }
        }

        return stateLabels;
    }

    // The state that each label of stateLabels starts, numbered by the same rule that FindStateLabels uses to place the
    // gotos, so a goto and its label always agree on the state
    private static Dictionary<DeclarationKey, int> NumberLabelStates(SyntaxList<StatementSyntax> statements,
        HashSet<DeclarationKey> stateLabels, SemanticModel? model)
    {
        var stateOfStatement = new int[statements.Count];
        NumberStates(statements, stateLabels, model, stateOfStatement);
        var labelStates = new Dictionary<DeclarationKey, int>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LabeledStatementSyntax labeled &&
                LoweringIdentity.ForDeclaration(labeled, model) is var key &&
                stateLabels.Contains(key))
            {
                labelStates[key] = stateOfStatement[i];
            }
        }

        return labelStates;
    }

    // The state each top-level statement falls in: a state label starts a new state at itself, a split point after itself.
    // This is the only definition of the numbering (see NumberLabelStates, FindGotosToChainedLabelsAcrossStates).
    private static void NumberStates(SyntaxList<StatementSyntax> statements, HashSet<DeclarationKey> stateLabels,
        SemanticModel? model, int[] stateOfStatement)
    {
        var state = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LabeledStatementSyntax labeled &&
                stateLabels.Contains(LoweringIdentity.ForDeclaration(labeled, model)))
            {
                state++;
            }

            stateOfStatement[i] = state;
            if (BarrierHelper.GetPhaseBarrierInvocation(statements[i]) != null)
            {
                state++;
            }
        }
    }

    // The gotos of each top-level statement that reach a label of labels: (statement index, goto, label key). The one
    // definition of "a goto of statement i to a label", for FindStateLabels and FindGotosToChainedLabelsAcrossStates
    private static ImmutableArray<(int Statement, GotoStatementSyntax Goto, DeclarationKey Target)> FindGotos<TValue>(
        SyntaxList<StatementSyntax> statements, Dictionary<DeclarationKey, TValue> labels, SemanticModel? model)
    {
        var gotos = ImmutableArray.CreateBuilder<(int Statement, GotoStatementSyntax Goto, DeclarationKey Target)>();
        for (var i = 0; i < statements.Count; i++)
        {
            foreach (var node in statements[i].DescendantNodesAndSelf())
            {
                if (node is GotoStatementSyntax
                    {
                        RawKind: (int)SyntaxKind.GotoStatement, Expression: IdentifierNameSyntax target
                    } jump &&
                    LoweringIdentity.ForReference(target, model) is var key &&
                    labels.ContainsKey(key))
                {
                    gotos.Add((i, jump, key));
                }
            }
        }

        return gotos.ToImmutable();
    }

    // The gotos that reach an inner label of chained labels (second of first: second: s) from another state than the
    // top-level statement of the chain, numbered by NumberStates. Only a top-level label can start a state, so such a
    // goto would stay a C# / C++ goto into another case block (GSH0121, which reports it from this list)
    private static ImmutableArray<ChainedLabelGoto> FindGotosToChainedLabelsAcrossStates(
        SyntaxList<StatementSyntax> statements, HashSet<DeclarationKey> stateLabels, SemanticModel? model)
    {
        // Each inner label: the top-level statement of its chain, its name and the first label of the chain; made at the
        // first chain, since most bodies have none
        Dictionary<DeclarationKey, (int Statement, string Label, string OuterLabel)>? innerLabels = null;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not LabeledStatementSyntax chain)
            {
                continue;
            }

            for (var inner = chain.Statement as LabeledStatementSyntax;
                 inner != null;
                 inner = inner.Statement as LabeledStatementSyntax)
            {
                innerLabels ??= new Dictionary<DeclarationKey, (int Statement, string Label, string OuterLabel)>();
                innerLabels[LoweringIdentity.ForDeclaration(inner, model)] =
                    (i, inner.Identifier.ValueText, chain.Identifier.ValueText);
            }
        }

        if (innerLabels is null)
        {
            return ImmutableArray<ChainedLabelGoto>.Empty;
        }

        var stateOfStatement = new int[statements.Count];
        NumberStates(statements, stateLabels, model, stateOfStatement);
        var rejected = ImmutableArray.CreateBuilder<ChainedLabelGoto>();
        foreach (var (statement, jump, target) in FindGotos(statements, innerLabels, model))
        {
            var (labelStatement, label, outerLabel) = innerLabels[target];
            if (stateOfStatement[statement] != stateOfStatement[labelStatement])
            {
                rejected.Add(new ChainedLabelGoto(jump, label, outerLabel));
            }
        }

        return rejected.ToImmutable();
    }
}
