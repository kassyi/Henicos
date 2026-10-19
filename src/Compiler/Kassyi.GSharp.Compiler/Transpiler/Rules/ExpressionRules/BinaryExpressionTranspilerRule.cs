namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles C# binary expressions (arithmetic, logical, bitwise, comparison, and null-coalescing <c>??</c>) into target dialect operations.
/// </summary>
/// <remarks>
/// <para>
/// Null-coalescing expressions (<c>??</c>) are lowered into immediately invoked C++ lambdas with temporary value binding.
/// </para>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class BinaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<BinaryExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="BinaryExpressionTranspilerRule"/>.
    /// </summary>
    public BinaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            using (context.Emitter.Writer.ImmediatelyInvokedLambda())
            {
                context.Emitter.Writer.Append("auto __coalesce_tmp = ");
                context.Visitor.Visit(node.Left);
                context.Emitter.Writer.AppendSemiColonLine();

                context.Emitter.Writer.Append("return __coalesce_tmp.HasValue ? __coalesce_tmp.Value : ");
                context.Visitor.Visit(node.Right);
                context.Emitter.Writer.AppendSemiColonLine();
            }

            return;
        }

        if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression))
        {
            var leftType = context.Query.SemanticModel.GetTypeSymbol(node.Left);
            var rightType = context.Query.SemanticModel.GetTypeSymbol(node.Right);

            var leftIsNull = node.Left.IsKind(SyntaxKind.NullLiteralExpression);
            var rightIsNull = node.Right.IsKind(SyntaxKind.NullLiteralExpression);

            if (leftIsNull && (rightType != null && (rightType.IsArrayLikeType() || rightType.IsNullableArrayLike())))
            {
                var op = node.IsKind(SyntaxKind.EqualsExpression) ? "==" : "!=";
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(node.Right);
                    context.Emitter.Writer.Append($".data {op} nullptr");
                }

                return;
            }

            if (rightIsNull && (leftType != null && (leftType.IsArrayLikeType() || leftType.IsNullableArrayLike())))
            {
                var op = node.IsKind(SyntaxKind.EqualsExpression) ? "==" : "!=";
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(node.Left);
                    context.Emitter.Writer.Append($".data {op} nullptr");
                }

                return;
            }
        }

        context.Visitor.Visit(node.Left);

        var opText = node.OperatorToken.Text;
        if (opText.Length > 0 && char.IsLetter(opText[0]))
        {
            context.Emitter.Writer.Append($" {opText} ");
        }
        else
        {
            context.Emitter.Writer.AppendOperator(opText);
        }

        context.Visitor.Visit(node.Right);
    }
}
