namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles pattern matching expressions (such as <c>x is null</c> or <c>x is not null</c>) for array-like and nullable array-like types.
/// </summary>
public sealed class IsPatternExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<IsPatternExpressionSyntax>
{
    public override void Transpile(IsPatternExpressionSyntax node, ref TranspileContext context)
    {
        if (NullRepresentation.IsArrayNullComparable(context.Query.SemanticModel.GetTypeSymbol(node.Expression)))
        {
            if (node.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal } &&
                literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                NullRepresentation.AppendArrayNullTest(node.Expression, isNull: true, ref context);
                return;
            }

            if (node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notPattern &&
                notPattern.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax notLiteral } &&
                notLiteral.IsKind(SyntaxKind.NullLiteralExpression))
            {
                NullRepresentation.AppendArrayNullTest(node.Expression, isNull: false, ref context);
                return;
            }
        }

        context.Visitor.DefaultVisit(node);
    }
}
