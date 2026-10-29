namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Flattens a <c>switch</c> statement whose sections hold a split point (a barrier, a warp shuffle, a call that may
/// suspend) into a dispatch, labeled sections and an end label, so that <see cref="StateMachineLowerer"/> sees the split
/// point at the top level of the body (#708).
/// </summary>
/// <remarks>
/// [WHY] This is how Roslyn lowers a switch statement before its async and iterator rewriters run
/// (<c>LocalRewriter_PatternSwitchStatement.LowerSwitchStatement</c>): the governing expression is evaluated once into a
/// temporary, the dispatch jumps to the label of the matching section, each section follows its label, <c>break</c>
/// jumps to the break label at the end, and <c>goto case</c> / <c>goto default</c> jump to the label of the section
/// that holds that case (<c>SwitchBinder.BindGotoCaseOrDefault</c>).
/// <code>
/// { var __gsharp_switch_temp_N = &lt;expr&gt;; if (__gsharp_switch_temp_N == &lt;c&gt;) goto __gsharp_SwitchSection_N_&lt;i&gt;; ... goto &lt;default section or end&gt;; }
/// __gsharp_SwitchSection_N_0: ; &lt;section 0&gt;
/// ...
/// __gsharp_SwitchEnd_N: ;
/// </code>
/// The dispatch is a chain of <c>if</c> statements, not a nested switch: a goto to a label that starts a state becomes
/// <c>goto case &lt;state&gt;</c>, which inside a nested switch would bind to that switch. G# supports only constant
/// case labels and <c>default</c>, so <c>==</c> against the constant is the C# match; a switch with a pattern label is
/// left as it is, and the transpiler reports the label. The dispatch is a block of its own, so its temporary stays a
/// local of the state that dispatches instead of a context field. C# requires the end of every section to be
/// unreachable, so no section falls into the next one.
/// </remarks>
internal static class SwitchStatementFlattener
{
    /// <summary>Whether every label of <paramref name="node"/> is a constant case label or <c>default</c>.</summary>
    public static bool CanFlatten(SwitchStatementSyntax node) =>
        node.Sections
            .AsStackEnumerable()
            .All(static section => section.Labels
                .AsStackEnumerable()
                .All(static label => label is CaseSwitchLabelSyntax or DefaultSwitchLabelSyntax)
            );

    /// <summary>Flattens <paramref name="node"/> into the statements of a block.</summary>
    /// <param name="node">The switch statement, whose labels <see cref="CanFlatten"/> accepts.</param>
    /// <param name="id">A number that makes the labels and the temporary of this switch unique in the body.</param>
    /// <param name="expression">The governing expression, already lowered.</param>
    /// <param name="sections">The statements of each section, already lowered, in the order of the sections.</param>
    /// <param name="semanticModel">Matches <c>goto case</c> to its case label by constant value, or <see langword="null"/> to match equivalent expressions.</param>
    public static BlockSyntax Flatten(SwitchStatementSyntax node, int id, ExpressionSyntax expression,
        ImmutableArray<SyntaxList<StatementSyntax>> sections, SemanticModel? semanticModel)
    {
        var endLabel = $"{GSharpInternalNames.Lowering.LabelPrefix}SwitchEnd_{id}";
        var temp = SyntaxFactory.IdentifierName($"{GSharpInternalNames.Lowering.SwitchTempPrefix}{id}");
        var sectionLabels = new string[node.Sections.Count];
        var caseTargets = ImmutableArray.CreateBuilder<(object Key, string Label)>();
        string? defaultLabel = null;

        // var <temp> = <expression>;
        var dispatch = ImmutableArray.CreateBuilder<StatementSyntax>();
        dispatch.Add(CreateTempDeclaration(temp.Identifier.ValueText, expression));
        for (var i = 0; i < node.Sections.Count; i++)
        {
            sectionLabels[i] = $"{GSharpInternalNames.Lowering.LabelPrefix}SwitchSection_{id}_{i}";
            foreach (var label in node.Sections[i].Labels)
            {
                if (label is CaseSwitchLabelSyntax caseLabel)
                {
                    // if (<temp> == <constant>) goto <section label>;
                    dispatch.Add(
                        SyntaxFactory.IfStatement(
                            SyntaxFactory.BinaryExpression(
                                SyntaxKind.EqualsExpression,
                                temp,
                                AsOperand(caseLabel.Value.WithoutTrivia())
                            ),
                            Goto(sectionLabels[i])
                        )
                    );
                    caseTargets.Add((GetCaseKey(caseLabel.Value, semanticModel), sectionLabels[i]));
                }
                else
                {
                    defaultLabel = sectionLabels[i];
                }
            }
        }

        dispatch.Add(Goto(defaultLabel ?? endLabel));

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        statements.Add(SyntaxFactory.Block(dispatch));
        var jumps = new SwitchJumpRewriter(endLabel, defaultLabel, caseTargets.ToImmutable(), semanticModel);
        for (var i = 0; i < sections.Length; i++)
        {
            statements.Add(Label(sectionLabels[i]));
            foreach (var statement in sections[i])
            {
                statements.Add(jumps.Visit(statement) as StatementSyntax
                               ?? throw new InvalidOperationException("Switch flattening removed a statement."));
            }
        }

        statements.Add(Label(endLabel));
        return SyntaxFactory.Block(statements);
    }

    // [WHY] goto case names the case by its constant value (`goto case Last;` reaches `case 2:` when Last is 2), and C#
    // converts both to the governing type, so integral values are compared as numbers whatever their type. Without a
    // semantic model, or for a value that does not resolve, the expression itself is the key (see IsSameCase).
    private static object GetCaseKey(ExpressionSyntax value, SemanticModel? semanticModel)
    {
        if (semanticModel?.GetSafeConstantValue(value) is { HasValue: true, Value: { } constant })
        {
            return constant switch
            {
                bool or string => constant,
                char character => (decimal)character,
                float or double => Convert.ToDouble(constant, CultureInfo.InvariantCulture),
                IConvertible integral => integral.ToDecimal(CultureInfo.InvariantCulture),
                _ => constant
            };
        }

        return value;
    }

    // Two keys of GetCaseKey name the same case: equal constants, or equivalent expressions when they do not resolve
    private static bool IsSameCase(object caseKey, object gotoKey) =>
        caseKey is ExpressionSyntax caseValue
            ? gotoKey is ExpressionSyntax gotoValue && SyntaxFactory.AreEquivalent(caseValue, gotoValue)
            : caseKey.Equals(gotoKey);

    // A case value that is not a primary expression is parenthesized, so that == compares the whole value
    private static ExpressionSyntax AsOperand(ExpressionSyntax value) =>
        value is LiteralExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax
            or ParenthesizedExpressionSyntax or CastExpressionSyntax or InvocationExpressionSyntax
            ? value
            : SyntaxFactory.ParenthesizedExpression(value);

    // goto <label>;
    private static GotoStatementSyntax Goto(string label) =>
        SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(label));

    // <label>: ;
    private static LabeledStatementSyntax Label(string label) =>
        SyntaxFactory.LabeledStatement(label, SyntaxFactory.EmptyStatement());

    // var <name> = <value>;
    private static LocalDeclarationStatementSyntax CreateTempDeclaration(string name, ExpressionSyntax value) =>
        SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(value.WithoutTrivia()))
                )
            )
        );

    /// <summary>
    /// Rewrites the jumps of a flattened switch's own sections: <c>break</c> to <c>goto</c> the end label,
    /// <c>goto case</c> and <c>goto default</c> to <c>goto</c> the label of their section.
    /// </summary>
    /// <remarks>
    /// A <c>break</c> in a nested loop or switch, and a <c>goto case</c> in a nested switch, belong to that statement.
    /// A <c>continue</c> belongs to the enclosing loop, which <see cref="LoopRewriter"/> lowers afterwards. Lambdas and
    /// local functions are not part of the state machine (GSH0062). The new gotos keep the original span of the jump.
    /// </remarks>
    private sealed class SwitchJumpRewriter(
        string endLabel,
        string? defaultLabel,
        ImmutableArray<(object Key, string Label)> caseTargets,
        SemanticModel? semanticModel)
        : CSharpSyntaxRewriter
    {
        private int _breakDepth;
        private int _switchDepth;

        public override SyntaxNode? VisitBreakStatement(BreakStatementSyntax node) =>
            _breakDepth > 0 ? node : GotoFrom(node, endLabel);

        public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
        {
            if (_switchDepth > 0 || node.IsKind(SyntaxKind.GotoStatement))
            {
                return base.VisitGotoStatement(node);
            }

            var target = node.IsKind(SyntaxKind.GotoDefaultStatement)
                ? defaultLabel
                : FindCaseLabel(node.Expression);
            return GotoFrom(node, target ?? throw new InvalidOperationException(
                "A goto case or goto default of a flattened switch has no section to jump to."));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) =>
            VisitNested(node, isSwitch: false, base.VisitWhileStatement);

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
            VisitNested(node, isSwitch: false, base.VisitDoStatement);

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
            VisitNested(node, isSwitch: false, base.VisitForStatement);

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
            VisitNested(node, isSwitch: false, base.VisitForEachStatement);

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
            VisitNested(node, isSwitch: false, base.VisitForEachVariableStatement);

        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node) =>
            VisitNested(node, isSwitch: true, base.VisitSwitchStatement);

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

        private string? FindCaseLabel(ExpressionSyntax? value)
        {
            if (value == null)
            {
                return null;
            }

            var key = GetCaseKey(value, semanticModel);
            return caseTargets
                .AsStackEnumerable()
                .TryFirst(key, static (target, k) => IsSameCase(target.Key, k), out var match)
                ? match.Label
                : null;
        }

        private SyntaxNode? VisitNested<TNode>(TNode node, bool isSwitch, Func<TNode, SyntaxNode?> visitBase)
            where TNode : SyntaxNode
        {
            _breakDepth++;
            _switchDepth += isSwitch ? 1 : 0;
            try
            {
                return visitBase(node);
            }
            finally
            {
                _breakDepth--;
                _switchDepth -= isSwitch ? 1 : 0;
            }
        }

        // goto <label>; with the trivia and the original span of the jump it replaces
        private static GotoStatementSyntax GotoFrom(StatementSyntax jump, string label) =>
            Goto(label)
                .WithTriviaFrom(jump)
                .WithAdditionalAnnotations(jump.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
    }
}
