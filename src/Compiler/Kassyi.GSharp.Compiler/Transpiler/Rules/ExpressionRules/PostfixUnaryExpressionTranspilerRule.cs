namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles postfix unary expressions (<c>x++</c>, <c>x--</c>) into target dialect syntax.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class PostfixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PostfixUnaryExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="PostfixUnaryExpressionTranspilerRule"/>.
    /// </summary>
    public PostfixUnaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(PostfixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        if ((node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression)) &&
            ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Operand, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.IncrementOrDecrementOfComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        context.Visitor.Visit(node.Operand);
        context.Emitter.Writer.Append(node.OperatorToken.Text);
    }
}
