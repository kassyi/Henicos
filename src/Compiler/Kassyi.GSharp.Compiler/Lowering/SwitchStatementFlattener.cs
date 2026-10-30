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
/// &lt;section consts&gt;
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
/// <para>
/// A const declared directly in a section is in scope in every section and in the case labels, and C# reads it without
/// an assignment (#795). Its section starts a state of its own once flattened, so a const hoisted into the context would
/// be assigned only on the path through its section; the consts are declared ahead of the dispatch instead, in source
/// order (a const reads only earlier consts). Their initializers are constants, so the move changes no evaluation.
/// </para>
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
        var dispatch = BuildDispatch(node, id, expression, semanticModel);
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        foreach (var section in sections)
        {
            section
                .AsStackEnumerable()
                .Where(IsSectionConstant)
                .AddTo(statements);
        }

        statements.Add(dispatch.Block);
        LayOutSections(sections, dispatch, semanticModel, statements);
        statements.Add(Label(dispatch.EndLabel));
        return SyntaxFactory.Block(statements);
    }

    /// <summary>Builds <c>goto &lt;label&gt;;</c>.</summary>
    public static GotoStatementSyntax Goto(string label) =>
        SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(label));

    // The temporary, one comparison per case label in the order of the source and the jump to the default section or the
    // end; the labels that the sections and the jumps of SwitchJumpRewriter go to
    private static SwitchDispatch BuildDispatch(SwitchStatementSyntax node, int id, ExpressionSyntax expression,
        SemanticModel? semanticModel)
    {
        var endLabel = $"{GSharpInternalNames.Lowering.LabelPrefix}SwitchEnd_{id}";
        var temp = SyntaxFactory.IdentifierName($"{GSharpInternalNames.Lowering.SwitchTempPrefix}{id}");
        var sectionLabels = new string[node.Sections.Count];
        var caseTargets = ImmutableArray.CreateBuilder<SwitchCaseTarget>();
        string? defaultLabel = null;

        // var <temp> = <expression>;
        var dispatch = ImmutableArray.CreateBuilder<StatementSyntax>();
        dispatch.Add(CreateTempDeclaration(temp.Identifier.ValueText, expression));
        for (var i = 0; i < node.Sections.Count; i++)
        {
            sectionLabels[i] = $"{GSharpInternalNames.Lowering.LabelPrefix}SwitchSection_{id}_{i}";
            foreach (var label in node.Sections[i].Labels)
            {
                if (label is not CaseSwitchLabelSyntax caseLabel)
                {
                    defaultLabel = sectionLabels[i];
                    continue;
                }

                // if (<case condition>) goto <section label>;
                dispatch.Add(
                    SyntaxFactory.IfStatement(
                        CreateCaseCondition(temp, caseLabel.Value, semanticModel),
                        Goto(sectionLabels[i])
                    )
                );
                caseTargets.Add(
                    new SwitchCaseTarget(
                        semanticModel?.GetDeclaredSymbol<ILabelSymbol>(caseLabel),
                        caseLabel.Value,
                        sectionLabels[i]
                    )
                );
            }
        }

        dispatch.Add(Goto(defaultLabel ?? endLabel));

        // [WHY] The dispatch carries the span of the switch, so a diagnostic on the temporary or a comparison is
        // reported at the switch (Reporter.GetReportLocation takes the nearest annotated ancestor)
        var block = SyntaxFactory.Block(dispatch)
            .WithAdditionalAnnotations(node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
        return new SwitchDispatch(block, sectionLabels, caseTargets.ToImmutable(), defaultLabel, endLabel);
    }

    // <temp> == <constant>, or <temp> != <temp> for a NaN constant
    // [WHY] C# matches a NaN value against case float.NaN / double.NaN (Roslyn's switch lowering tests it with IsNaN),
    // which == never does. temp != temp holds only for NaN, in C# and in the C++ of the native path (built without
    // fast-math), and the native dialect has no float.IsNaN
    private static ExpressionSyntax CreateCaseCondition(IdentifierNameSyntax temp, ExpressionSyntax value,
        SemanticModel? semanticModel)
    {
        var isNaN = semanticModel?.GetSafeConstantValue(value) is { HasValue: true, Value: float or double } constant &&
                    double.IsNaN(Convert.ToDouble(constant.Value, CultureInfo.InvariantCulture));
        return isNaN
            ? SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression, temp, temp)
            : SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, temp, AsOperand(value.WithoutTrivia()));
    }

    // <section label i>: ; <section i without its consts, jumps rewritten> ...
    private static void LayOutSections(ImmutableArray<SyntaxList<StatementSyntax>> sections, SwitchDispatch dispatch,
        SemanticModel? semanticModel, ImmutableArray<StatementSyntax>.Builder statements)
    {
        var jumps = new SwitchJumpRewriter(dispatch.EndLabel, dispatch.DefaultLabel, dispatch.CaseTargets, semanticModel);
        for (var i = 0; i < sections.Length; i++)
        {
            statements.Add(Label(dispatch.SectionLabels[i]));
            foreach (var statement in sections[i])
            {
                if (IsSectionConstant(statement))
                {
                    continue;
                }

                statements.Add(jumps.Visit(statement) as StatementSyntax
                               ?? throw new InvalidOperationException("Switch flattening removed a statement."));
            }
        }
    }

    private static bool IsSectionConstant(StatementSyntax statement) =>
        statement is LocalDeclarationStatementSyntax { IsConst: true };

    // A case value that is not a primary expression is parenthesized, so that == compares the whole value
    private static ExpressionSyntax AsOperand(ExpressionSyntax value) =>
        value is LiteralExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax
            or ParenthesizedExpressionSyntax or CastExpressionSyntax or InvocationExpressionSyntax
            ? value
            : SyntaxFactory.ParenthesizedExpression(value);

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

    // The dispatch block of a flattened switch and the labels it and the jumps of its sections go to
    private readonly record struct SwitchDispatch(
        BlockSyntax Block,
        string[] SectionLabels,
        ImmutableArray<SwitchCaseTarget> CaseTargets,
        string? DefaultLabel,
        string EndLabel);

    /// <summary>A case label of a flattened switch: its label symbol, its value and the label of its section.</summary>
    /// <param name="Symbol">The label symbol that a <c>goto case</c> binds to, or <see langword="null"/> without a semantic model.</param>
    /// <param name="Value">The case value, matched by equivalence when a symbol does not resolve.</param>
    /// <param name="Label">The label of the section that holds the case.</param>
    internal readonly record struct SwitchCaseTarget(ILabelSymbol? Symbol, ExpressionSyntax Value, string Label);
}
