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
    public static readonly PostfixUnaryExpressionTranspilerRule Instance = new();

    private PostfixUnaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(PostfixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        context.Visitor.Visit(node.Operand);
        context.Writer.Append(node.OperatorToken.Text);
    }
}
