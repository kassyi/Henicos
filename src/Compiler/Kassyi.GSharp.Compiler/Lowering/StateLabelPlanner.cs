namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Decides which top-level labels of a flattened body start a state of the CPU state machine.</summary>
/// <remarks>
/// [WHY] A state machine state is a <c>case</c> block of a <c>switch</c>, and C# / C++ cannot jump into another block with
/// <c>goto</c>, so a label that a <c>goto</c> in another state reaches has to start a state of its own (the
/// <c>goto</c> becomes <c>goto case</c>). Any other label stays a C# / C++ label inside its state, so the locals declared
/// before it stay in scope after it, as in Roslyn's async lowering, where only the suspension points start states.
/// <list type="bullet">
///   <item><description>A label that lowering adds (<c>__gsharp_</c>) always starts a state.</description></item>
///   <item><description>A label of the user starts one when a <c>goto</c> to it is in another state.</description></item>
/// </list>
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
/// The type is internal: the lowering passes that take its result (<see cref="ContextHoister"/>,
/// <see cref="StateMachineLowerer"/>) are public, but they take the set as <see cref="HashSet{T}"/> of
/// <see cref="DeclarationKey"/> and find it themselves when it is not given, so no caller outside the compiler needs it.
/// </para>
/// </remarks>
internal static class StateLabelPlanner
{
    /// <summary>Returns the keys (<see cref="LoweringIdentity.ForDeclaration"/>) of the top-level labels of <paramref name="body"/> that start a state.</summary>
    /// <param name="body">The flattened body that <see cref="StateMachineLowerer"/> lowers.</param>
    /// <param name="model">The semantic model that keys the labels by symbol, or <see langword="null"/> to key them by name.</param>
    public static HashSet<DeclarationKey> FindStateLabels(BlockSyntax body, SemanticModel? model)
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

        var labelCounts = model == null ? CountLabelNames(body) : null;
        foreach (var statement in statements)
        {
            if (statement is LabeledStatementSyntax labeled &&
                (IsLoweringLabel(labeled) || labelCounts?[labeled.Identifier.ValueText] > 1))
            {
                stateLabels.Add(LoweringIdentity.ForDeclaration(labeled, model));
            }
        }

        // The gotos of each top-level statement that reach a top-level label: (statement index, label key)
        var gotos = ImmutableArray.CreateBuilder<(int Statement, DeclarationKey Target)>();
        for (var i = 0; i < statements.Count; i++)
        {
            foreach (var node in statements[i].DescendantNodesAndSelf())
            {
                if (node is GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoStatement, Expression: IdentifierNameSyntax target } &&
                    LoweringIdentity.ForReference(target, model) is var key &&
                    labels.ContainsKey(key))
                {
                    gotos.Add((i, key));
                }
            }
        }

        var stateOfStatement = new int[statements.Count];
        var changed = gotos.Count > 0;
        while (changed)
        {
            NumberStates(statements, stateLabels, model, stateOfStatement);
            changed = false;
            foreach (var (statement, target) in gotos)
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

    // The state each top-level statement falls in: a state label starts a new state at itself, a split point after itself
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

    private static bool IsLoweringLabel(LabeledStatementSyntax labeled) =>
        labeled.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.LabelPrefix, StringComparison.Ordinal);

    // How many labels of each name the body declares, nested ones included
    private static Dictionary<string, int> CountLabelNames(BlockSyntax body)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodes())
        {
            if (node is LabeledStatementSyntax labeled)
            {
                var name = labeled.Identifier.ValueText;
                counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
            }
        }

        return counts;
    }
}
