namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles parenthesized expressions (<c>(expr)</c>) into target dialect parenthesized syntax.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ParenthesizedExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ParenthesizedExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ParenthesizedExpressionTranspilerRule"/>.
    /// </summary>
    public static readonly ParenthesizedExpressionTranspilerRule Instance = new();

    private ParenthesizedExpressionTranspilerRule() { }

    /// <inheritdoc />
    public override void Transpile(ParenthesizedExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Expression);
        }
    }
}
