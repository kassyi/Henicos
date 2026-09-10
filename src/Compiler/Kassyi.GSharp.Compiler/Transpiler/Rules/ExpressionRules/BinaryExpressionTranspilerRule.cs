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
    public static readonly BinaryExpressionTranspilerRule Instance = new();

    private BinaryExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.Append("() ");
            using (context.Writer.Scope())
            {
                context.Writer.Append("auto __coalesce_tmp = ");
                context.Visitor.Visit(node.Left);
                context.Writer.AppendSemiColonLine();

                context.Writer.Append("return __coalesce_tmp.HasValue ? __coalesce_tmp.Value : ");
                context.Visitor.Visit(node.Right);
                context.Writer.AppendSemiColonLine();
            }

            context.Writer.AppendParentheses();

            return;
        }

        context.Visitor.Visit(node.Left);

        var opText = node.OperatorToken.Text;
        if (opText.Length > 0 && char.IsLetter(opText[0]))
        {
            context.Writer.Append($" {opText} ");
        }
        else
        {
            context.Writer.AppendOperator(opText);
        }

        context.Visitor.Visit(node.Right);
    }
}
