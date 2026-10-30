namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Keeps an operator that ends in <c>-</c> or <c>+</c> apart from an operand that begins with the same sign, which C++
/// would read as <c>--</c> or <c>++</c> (<c>x - -y</c>, <c>- -x</c>, <c>x + +y</c>). This is the one place where the
/// output has a space that is not part of a declaration (transpiler-emission.md §3).
/// </summary>
internal static class OperatorSpacing
{
    // <operator>, then ' ' when the C++ of <operand> begins with the operator's last character
    public static void AppendOperator(string op, ExpressionSyntax operand, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(op);
        if (op.Length > 0 && op[op.Length - 1] is '-' or '+' &&
            StartsWithSign(operand, op[op.Length - 1], context.Query.SemanticModel))
        {
            context.Emitter.Writer.Append(' ');
        }
    }

    // Whether the C++ of <expression> begins with <sign>: the leftmost operand, down to a prefix operator. A negated
    // numeric literal or an integer constant is written as its value, parenthesized when negative ((-5)), and an operator
    // written as a Prelude call (__gsharp_neg_i32(x), IntegerSemantics) begins with its name, so neither begins with a
    // sign.
    public static bool StartsWithSign(ExpressionSyntax expression, char sign, SemanticModel model)
    {
        while (true)
        {
            if (IntegerSemantics.IsWrittenAsCallOrConstant(expression, model))
            {
                return false;
            }

            switch (expression)
            {
                case PrefixUnaryExpressionSyntax prefix:
                    return !IsNegatedNumericLiteral(prefix) && prefix.OperatorToken.Text[0] == sign;
                case BinaryExpressionSyntax binary:
                    expression = binary.Left;
                    break;
                case AssignmentExpressionSyntax assignment:
                    expression = assignment.Left;
                    break;
                case PostfixUnaryExpressionSyntax postfix:
                    expression = postfix.Operand;
                    break;
                case ConditionalExpressionSyntax conditional:
                    expression = conditional.Condition;
                    break;
                case MemberAccessExpressionSyntax access:
                    expression = access.Expression;
                    break;
                case ElementAccessExpressionSyntax element:
                    expression = element.Expression;
                    break;
                case InvocationExpressionSyntax invocation:
                    expression = invocation.Expression;
                    break;
                default:
                    return false;
            }
        }
    }

    /// <summary>Whether <paramref name="node"/> is <c>-</c> applied to a numeric literal (<c>-5</c>, <c>-2147483648</c>).</summary>
    public static bool IsNegatedNumericLiteral(PrefixUnaryExpressionSyntax node) =>
        node.IsKind(SyntaxKind.UnaryMinusExpression) && node.Operand.IsKind(SyntaxKind.NumericLiteralExpression);
}
