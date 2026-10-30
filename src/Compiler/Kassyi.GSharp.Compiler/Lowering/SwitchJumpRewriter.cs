using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Rewrites the jumps of the sections of a switch that <see cref="SwitchStatementFlattener"/> flattens: <c>break</c> to
/// <c>goto</c> the end label, <c>goto case</c> and <c>goto default</c> to <c>goto</c> the label of their section.
/// </summary>
/// <remarks>
/// A <c>break</c> in a nested loop or switch, and a <c>goto case</c> in a nested switch, belong to that statement.
/// A <c>continue</c> belongs to the enclosing loop, which <see cref="LoopRewriter"/> lowers afterwards. Lambdas and
/// local functions are not part of the state machine (GSH0062). The new gotos keep the original span of the jump.
/// </remarks>
/// <param name="endLabel">The label after the last section, the target of <c>break</c>.</param>
/// <param name="defaultLabel">The label of the default section, or <see langword="null"/> when the switch has none.</param>
/// <param name="caseTargets">The case labels of the switch, with the labels of their sections.</param>
/// <param name="semanticModel">Binds a <c>goto case</c> to its case label, or <see langword="null"/> to match equivalent expressions.</param>
internal sealed class SwitchJumpRewriter(
    string endLabel,
    string? defaultLabel,
    ImmutableArray<SwitchStatementFlattener.SwitchCaseTarget> caseTargets,
    SemanticModel? semanticModel)
    : CSharpSyntaxRewriter
{
    private int _breakDepth;
    private int _switchDepth;

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        switch (node)
        {
            // Not visited only to save the walk: C# allows no break or goto case of this switch inside a lambda or a local
            // function, so skipping them changes no output (an equivalent mutant)
            case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax:
                return node;
            case SwitchStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                or CommonForEachStatementSyntax:
                var isSwitch = node is SwitchStatementSyntax;
                _breakDepth++;
                _switchDepth += isSwitch ? 1 : 0;
                try
                {
                    return base.Visit(node);
                }
                finally
                {
                    _breakDepth--;
                    _switchDepth -= isSwitch ? 1 : 0;
                }
            default:
                return base.Visit(node);
        }
    }

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
            : FindCaseLabel(node);
        return GotoFrom(node, target ?? throw new InvalidOperationException(
            "A goto case or goto default of a flattened switch has no section to jump to."));
    }

    // [WHY] C# binds goto case to the case label whose constant equals its value converted to the governing type
    // (goto case 1.5 reaches case 1 of an int switch, goto case 0.1 reaches case 0.1f of a float switch); the label
    // symbol that Roslyn binds (IBranchOperation.Target) is that case label's declared symbol, so no conversion is
    // repeated here. Without a semantic model, or when the goto does not resolve, equivalent case expressions match.
    private string? FindCaseLabel(GotoStatementSyntax node)
    {
        if (semanticModel?.GetSafeOperation(node) is IBranchOperation { Target: { } target } &&
            caseTargets
                .AsStackEnumerable()
                .TryFirst(target, static (c, t) => c.Symbol.IsEqualTo(t), out var bound))
        {
            return bound.Label;
        }

        return node.Expression is { } value &&
               caseTargets
                   .AsStackEnumerable()
                   .TryFirst(value, static (c, v) => SyntaxFactory.AreEquivalent(c.Value, v), out var equivalent)
            ? equivalent.Label
            : null;
    }

    // goto <label>; with the trivia and the original span of the jump it replaces
    private static GotoStatementSyntax GotoFrom(StatementSyntax jump, string label) =>
        SwitchStatementFlattener.Goto(label)
            .WithTriviaFrom(jump)
            .WithAdditionalAnnotations(jump.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
}
