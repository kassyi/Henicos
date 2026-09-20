namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles prefix unary expressions (<c>++x</c>, <c>--x</c>, <c>!x</c>, <c>~x</c>, <c>-x</c>, <c>+x</c>, and pointer indirection <c>*ptr</c>) into target dialect syntax.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class PrefixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PrefixUnaryExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="PrefixUnaryExpressionTranspilerRule"/>.
    /// </summary>
    public static readonly PrefixUnaryExpressionTranspilerRule Instance = new();

    private PrefixUnaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(PrefixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            context.Writer.AppendOperator("*");
            context.Visitor.Visit(node.Operand);
            return;
        }

        var op = node.OperatorToken.Text;
        context.Writer.Append(op);
        context.Visitor.Visit(node.Operand);
    }
}
