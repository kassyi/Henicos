namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Decides whether an identity or static constant of a type parameter (<c>T.MinValue</c>, <c>T.One</c>) takes part in
/// an operation that C# would fold at compile time, which the closed copy must leave to run time
/// (<see cref="GenericCopyPlanner"/> writes such a constant so that C# cannot fold it).
/// </summary>
internal static class ConstantOperandForms
{
    /// <summary>
    /// Whether the constant <paramref name="access"/> is an operand of an operator on constants, where the copy needs
    /// the run-time result of the declaration: the exception of a division by zero or an overflow, or the wrapped
    /// value of an unchecked negation (C# reports an overflow of a constant expression outside an explicit unchecked
    /// context).
    /// </summary>
    /// <param name="access">The member access of the constant.</param>
    /// <param name="model">The semantic model of the declaration.</param>
    /// <param name="resolveStatic">The member of a constraint that a member access reaches, if it reaches one.</param>
    public static bool IsOperandOfConstantOperation(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        Func<MemberAccessExpressionSyntax, ConstrainedMember?> resolveStatic)
    {
        // The operators between the constant and the operation that may overflow: parentheses, checked and
        // unchecked, and the unary operators, which keep the expression constant
        SyntaxNode operand = access;
        var negated = false;
        while (operand.Parent is { } parent && IsConstantPreserving(parent))
        {
            negated |= parent.IsKind(SyntaxKind.UnaryMinusExpression);
            operand = parent;
        }

        // -c overflows for MinValue and for an unsigned c other than 0
        if (negated)
        {
            return true;
        }

        if (operand.Parent is not BinaryExpressionSyntax binary ||
            binary.Kind() is not (SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
                SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression))
        {
            return false;
        }

        return IsConstantLike(binary.Left == operand ? binary.Right : binary.Left, model, resolveStatic);
    }

    // An operator that keeps a constant expression constant and may be the one that overflows (the unary minus) or
    // lies between the constant and it: parentheses, checked and unchecked, unary +, - and ~
    private static bool IsConstantPreserving(SyntaxNode node) =>
        node is ParenthesizedExpressionSyntax or CheckedExpressionSyntax or PrefixUnaryExpressionSyntax
        {
            RawKind: (int)SyntaxKind.UnaryMinusExpression or (int)SyntaxKind.UnaryPlusExpression or
                (int)SyntaxKind.BitwiseNotExpression
        };

    private static bool IsConstantLike(
        ExpressionSyntax expression,
        SemanticModel model,
        Func<MemberAccessExpressionSyntax, ConstrainedMember?> resolveStatic)
    {
        expression = expression.UnwrapParentheses();
        if (expression is PrefixUnaryExpressionSyntax unary && IsConstantPreserving(unary))
        {
            return IsConstantLike(unary.Operand, model, resolveStatic);
        }

        if (expression is CheckedExpressionSyntax checkedExpression)
        {
            return IsConstantLike(checkedExpression.Expression, model, resolveStatic);
        }

        return model.GetConstantValue(expression).HasValue ||
               (expression is MemberAccessExpressionSyntax access &&
                resolveStatic(access) is ConstrainedMember.IdentityConstant or ConstrainedMember.StaticMember);
    }
}
