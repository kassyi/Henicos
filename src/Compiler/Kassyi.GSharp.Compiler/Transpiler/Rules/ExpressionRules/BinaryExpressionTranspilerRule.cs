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
            NullRepresentation.AppendCoalesce(node.Left, node.Right, ref context);
            return;
        }

        if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression))
        {
            var isNull = node.IsKind(SyntaxKind.EqualsExpression);
            if (node.Left.IsKind(SyntaxKind.NullLiteralExpression) &&
                NullRepresentation.IsArrayNullComparable(context.Query.SemanticModel.GetTypeSymbol(node.Right)))
            {
                NullRepresentation.AppendArrayNullTest(node.Right, isNull, ref context);
                return;
            }

            if (node.Right.IsKind(SyntaxKind.NullLiteralExpression) &&
                NullRepresentation.IsArrayNullComparable(context.Query.SemanticModel.GetTypeSymbol(node.Left)))
            {
                NullRepresentation.AppendArrayNullTest(node.Left, isNull, ref context);
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
            context.Emitter.Writer.Append(opText);
        }

        context.Visitor.Visit(node.Right);
    }
}
