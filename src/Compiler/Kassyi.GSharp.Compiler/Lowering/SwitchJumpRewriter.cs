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
    ImmutableArray<SwitchCaseTarget> caseTargets,
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
        _breakDepth > 0 || LabeledJumpSyntax.GetLabelName(node) != null ? node : GotoFrom(node, endLabel);

    public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
    {
        if (_switchDepth > 0 || node.IsKind(SyntaxKind.GotoStatement))
        {
            return base.VisitGotoStatement(node);
        }

        var target = SwitchCaseLookup.FindTargetLabel(node, defaultLabel, caseTargets, semanticModel);
        return GotoFrom(node, target ?? throw new InvalidOperationException(
            "A goto case or goto default of a flattened switch has no section to jump to."));
    }

    // goto <label>; with the trivia and the original span of the jump it replaces
    private static GotoStatementSyntax GotoFrom(StatementSyntax jump, string label) =>
        LoweringSyntax.Goto(label).WithTriviaFrom(jump).WithOriginalSpanFrom(jump);
}
