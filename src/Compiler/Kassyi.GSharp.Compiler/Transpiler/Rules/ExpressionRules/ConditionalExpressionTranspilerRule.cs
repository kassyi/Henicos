
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
    public ConditionalExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        if (node.WhenTrue is RefExpressionSyntax || node.WhenFalse is RefExpressionSyntax)
        {
            // [WHY] A ref conditional is an lvalue in C#. Selecting between addresses and dereferencing the result
            // yields an equivalent C++ lvalue, so it works both as a value and as a 'ref' argument ('&(*(...))').
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Writer.Append('*');
                TranspileBranches(node, ref context);
            }

            return;
        }

        TranspileBranches(node, ref context);
    }

    private static void TranspileBranches(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
            context.Emitter.Writer.Append('?');

            // Handle 'ref' conditionals if present
            if (node.WhenTrue is RefExpressionSyntax refTrue)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, refTrue.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenTrue);
            }

            context.Emitter.Writer.Append(':');

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
