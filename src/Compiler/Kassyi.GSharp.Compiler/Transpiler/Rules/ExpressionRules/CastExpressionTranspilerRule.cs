namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles explicit C# type cast expressions into C-style parenthesized casts in target dialect code.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class CastExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<CastExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="CastExpressionTranspilerRule"/>.
    /// </summary>
    public CastExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(CastExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.AppendType(context.SemanticModel.GetTypeInfo(node.Type).Type);
        }

        context.Visitor.Visit(node.Expression);
    }
}
