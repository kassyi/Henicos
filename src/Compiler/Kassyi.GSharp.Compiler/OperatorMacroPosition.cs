using static Kassyi.GSharp.Compiler.OperatorMacroSyntax;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Where an operator macro expression is expanded: as the right side of an assignment statement, or as an operand of
/// an operator macro there.
/// </summary>
internal static class OperatorMacroPosition
{
    /// <summary>
    /// Why the operator macro expression <paramref name="operatorExpression"/> is not expanded where it is, or
    /// <see langword="null"/> when it is: as the right side of an assignment statement (inside parentheses and casts),
    /// or as an operand of an operator macro that is. <see cref="OperatorMacroResolver.TryResolve"/> reports what goes
    /// wrong there; anywhere else (a statement of its own such as <c>++x;</c>, an initializer, an argument) the
    /// operator would reach C++ as a struct operator that does not exist.
    /// </summary>
    public static string? GetPositionFailure(ExpressionSyntax operatorExpression, SemanticModel semanticModel)
    {
        var position = GetPosition(operatorExpression, semanticModel);
        if (position == Position.Expanded)
        {
            return null;
        }

        var op = operatorExpression is AssignmentExpressionSyntax assignment
            ? assignment.OperatorToken.Text
            : GetOperatorText(operatorExpression);
        return position == Position.UnderConversion
            ? OperatorMacroMessages.UnderConversion(op)
            : OperatorMacroMessages.NotExpandedHere(op);
    }

    private enum Position
    {
        Expanded,
        Elsewhere,

        // Inside a cast with a user-defined conversion, which the expansion would not apply (#759)
        UnderConversion
    }

    private static Position GetPosition(ExpressionSyntax operatorExpression, SemanticModel semanticModel)
    {
        // A compound assignment is the statement itself; OperatorMacroResolver.TryResolve reports it
        if (operatorExpression is AssignmentExpressionSyntax)
        {
            return operatorExpression.Parent is ExpressionStatementSyntax ? Position.Expanded : Position.Elsewhere;
        }

        SyntaxNode node = operatorExpression;
        while (node.Parent is ParenthesizedExpressionSyntax ||
               (node.Parent is CastExpressionSyntax cast && IsTransparentCast(cast, semanticModel)))
        {
            node = node.Parent;
        }

        return node.Parent switch
        {
            CastExpressionSyntax => Position.UnderConversion,
            AssignmentExpressionSyntax assignment when assignment.Right == node =>
                assignment.Parent is ExpressionStatementSyntax ? Position.Expanded : Position.Elsewhere,
            ExpressionSyntax parent and (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax)
                when GetMacroAttribute(GetOperator(parent, semanticModel).Method) != null =>
                GetPosition(parent, semanticModel),
            _ => Position.Elsewhere
        };
    }
}
