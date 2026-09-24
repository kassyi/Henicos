using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;/// <summary>
/// Transpiles C# ternary conditional expressions (<c>condition ? trueExpr : falseExpr</c>) into parenthesized C++ conditional expressions.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ConditionalExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ConditionalExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ConditionalExpressionTranspilerRule"/>.
    /// </summary>
    public static readonly ConditionalExpressionTranspilerRule Instance = new();

    private ConditionalExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
            context.Writer.Append('?');

            // Handle 'ref' conditionals if present
            if (node.WhenTrue is RefExpressionSyntax refTrue)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, refTrue.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenTrue);
            }

            context.Writer.Append(':');

            if (node.WhenFalse is RefExpressionSyntax refFalse)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, refFalse.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenFalse);
            }
        }
    }
}
