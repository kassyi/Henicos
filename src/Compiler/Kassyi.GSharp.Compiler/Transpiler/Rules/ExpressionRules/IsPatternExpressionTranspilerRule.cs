namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles pattern matching expressions (such as <c>x is null</c> or <c>x is not null</c>) for array-like and nullable array-like types.
/// </summary>
public sealed class IsPatternExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<IsPatternExpressionSyntax>
{
    public override void Transpile(IsPatternExpressionSyntax node, ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(node.Expression);
        if (type != null && (type.IsArrayLikeType() || type.IsNullableArrayLike()))
        {
            if (node.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal } &&
                literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(node.Expression);
                    context.Emitter.Writer.Append(".data == nullptr");
                }

                return;
            }

            if (node.Pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notPattern &&
                notPattern.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax notLiteral } &&
                notLiteral.IsKind(SyntaxKind.NullLiteralExpression))
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(node.Expression);
                    context.Emitter.Writer.Append(".data != nullptr");
                }

                return;
            }
        }

        context.Visitor.DefaultVisit(node);
    }
}
