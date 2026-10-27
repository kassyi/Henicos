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

        if (IsUnsequencedInCpp(node) && OperandsInterfere(node, ref context))
        {
            TranspileSequenced(node, ref context);
            return;
        }

        context.Visitor.Visit(node.Left);
        AppendOperator(node, node.Right, ref context);
        context.Visitor.Visit(node.Right);
    }

    // <right> is what follows the operator: the operand itself, or null for the name of a temporary
    private static void AppendOperator(BinaryExpressionSyntax node, ExpressionSyntax? right, ref TranspileContext context)
    {
        var opText = node.OperatorToken.Text;
        if (opText.Length > 0 && char.IsLetter(opText[0]))
        {
            context.Emitter.Writer.Append($" {opText} ");
        }
        else if (right != null)
        {
            OperatorSpacing.AppendOperator(opText, right, ref context);
        }
        else
        {
            context.Emitter.Writer.Append(opText);
        }
    }

    // [WHY] C# evaluates the left operand first (C# spec §12.4.1); C++ leaves the operands of the arithmetic, comparison
    // and bitwise operators unsequenced. <<, >> (C++17 [expr.shift]), &&, || and ?? (emitted in order) keep the C# order;
    // is and as have a type on the right; >>> has no C++ operator of its own.
    private static bool IsUnsequencedInCpp(BinaryExpressionSyntax node) =>
        !node.IsKind(SyntaxKind.LeftShiftExpression) &&
        !node.IsKind(SyntaxKind.RightShiftExpression) &&
        !node.IsKind(SyntaxKind.UnsignedRightShiftExpression) &&
        !node.IsKind(SyntaxKind.LogicalAndExpression) &&
        !node.IsKind(SyntaxKind.LogicalOrExpression) &&
        !node.IsKind(SyntaxKind.IsExpression) &&
        !node.IsKind(SyntaxKind.AsExpression);

    // The left operand changes what the right one reads, or the other way round (#608)
    private static bool OperandsInterfere(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        return EvaluationOrder.LastSpilledOperand(
            [ExpressionEffects.Of(node.Left, model), ExpressionEffects.Of(node.Right, model)]) >= 0;
    }

    // ([&]() { auto __gsharp_seq_0 = <left>; auto __gsharp_seq_1 = <right>; return __gsharp_seq_0 <op> __gsharp_seq_1; }())
    // auto keeps the C++ type of each operand, so the operator promotes them as it would in place.
    private static void TranspileSequenced(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        using var sequence = EvaluationOrder.BeginSequence(writer);
        var left = EvaluationOrder.DeclareTemp(ref context, node.Left, reference: false);
        var right = EvaluationOrder.DeclareTemp(ref context, node.Right, reference: false);
        sequence.Return();
        EvaluationOrder.AppendTempName(writer, left);
        AppendOperator(node, right: null, ref context);
        EvaluationOrder.AppendTempName(writer, right);
    }
}
