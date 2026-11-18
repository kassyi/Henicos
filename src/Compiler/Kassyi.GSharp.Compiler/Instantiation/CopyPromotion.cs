namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The numeric promotions of C# that a copy may show where the declaration has a type parameter: sbyte, byte, short,
/// ushort and char are promoted to int by a built-in operator, and <c>System.Math</c> has no char overload. A
/// promotion is allowed only where the plan narrows the result back or where the promoted operand is the whole of the
/// C# rule.
/// </summary>
internal static class CopyPromotion
{
    // [WHY] C# 12.4.7 promotes sbyte, byte, short, ushort and char to int on built-in operators, and System.Math
    // has no char overload (widening char to ushort for Min/Max, or int via numeric promotion for Sign).
    public static bool IsAllowedPromotion(
        CopyCheckContext context,
        SyntaxNode original,
        SyntaxNode copied,
        ITypeSymbol? expectedType,
        ITypeSymbol? expectedConverted,
        ITypeSymbol? copyConverted)
    {
        if (expectedType is null || copyConverted is null ||
            !context.Comparer.SameType(expectedType, expectedConverted) || !IsImplicitNumeric(context, copied))
        {
            return false;
        }

        if (IsMathCallCharPromotion(context, original, expectedType, copyConverted))
        {
            return true;
        }

        var promoted = BuiltinNumerics.GetPromotedSpecialType(expectedType);
        if (promoted == SpecialType.None || copyConverted.SpecialType != promoted)
        {
            return false;
        }

        var (operand, parent) = UnwrapParentWithOperand(context, original);
        return parent is not null &&
               (HasNarrowingRewrite(context, parent) ||
                IsPromotedUnnarrowedOperand(context, operand, parent, expectedType));
    }

    // The copy applies a built-in implicit numeric conversion to the node, not a user-defined one
    private static bool IsImplicitNumeric(CopyCheckContext context, SyntaxNode copied) =>
        copied is ExpressionSyntax expression &&
        context.CopyModel.GetConversion(expression) is
            { Exists: true, IsImplicit: true, IsNumeric: true, MethodSymbol: null };

    /// <summary>
    /// Whether a compound division or shift on a promoted type, which C# computes in int, has no narrowing rewrite in
    /// the plan: the copy would compute the result in the type of the instance.
    /// </summary>
    public static bool IsMissingNarrowing(CopyCheckContext context, SyntaxNode node) =>
        node is AssignmentExpressionSyntax assignment &&
        assignment.Kind() is SyntaxKind.DivideAssignmentExpression or SyntaxKind.LeftShiftAssignmentExpression or
            SyntaxKind.RightShiftAssignmentExpression or SyntaxKind.UnsignedRightShiftAssignmentExpression &&
        context.Model.GetTypeInfo(assignment).Type is { } type &&
        context.Map.Substitute(type) is { } closed &&
        BuiltinNumerics.IsPromoted(closed) &&
        IsPromotedBuiltinOperator(context, assignment, closed);

    // The rewrites that write the value of an operator in the type of the instance: the operands of C# are promoted
    private static bool IsNarrowing(CopyRewrite rewrite) =>
        rewrite is CopyRewrite.NarrowedOperator or CopyRewrite.NarrowedShift or CopyRewrite.TypedNegation or
            CopyRewrite.NarrowedNegation or CopyRewrite.NarrowedCompoundDivision or CopyRewrite.NarrowedCompoundShift;

    private static bool IsMathCallCharPromotion(
        CopyCheckContext context,
        SyntaxNode original,
        ITypeSymbol expectedType,
        ITypeSymbol copyConverted)
    {
        if (expectedType.SpecialType != SpecialType.System_Char ||
            UnwrapParentWithOperand(context, original).Parent is not ArgumentSyntax
            {
                Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call },
            } ||
            !context.OriginalNodes.TryGetValue(call, out var callIndex) ||
            !context.Plan.Rewrites.TryGetValue(callIndex, out var callRewrite) ||
            callRewrite is not CopyRewrite.MathCall math)
        {
            return false;
        }

        return (math.Name is "Max" or "Min" && copyConverted.SpecialType == SpecialType.System_UInt16) ||
               (math.Name == "Sign" &&
                copyConverted.SpecialType == BuiltinNumerics.GetPromotedSpecialType(expectedType));
    }

    private static bool HasNarrowingRewrite(CopyCheckContext context, SyntaxNode node) =>
        context.OriginalNodes.TryGetValue(node, out var nodeIndex) &&
        context.Plan.Rewrites.TryGetValue(nodeIndex, out var nodeRewrite) &&
        IsNarrowing(nodeRewrite);

    private static bool IsPromotedUnnarrowedOperand(
        CopyCheckContext context,
        SyntaxNode operand,
        SyntaxNode parent,
        ITypeSymbol expectedType)
    {
        var isUnnarrowedNode = parent switch
        {
            BinaryExpressionSyntax binary => binary.Kind() is SyntaxKind.EqualsExpression or
                SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression,
            AssignmentExpressionSyntax assignment =>
                ReferenceEquals(CopyCheckContext.UnwrapNode(assignment.Right), CopyCheckContext.UnwrapNode(operand)) &&
                assignment.Kind() is SyntaxKind.AddAssignmentExpression or
                    SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or
                    SyntaxKind.ModuloAssignmentExpression or SyntaxKind.AndAssignmentExpression or
                    SyntaxKind.OrAssignmentExpression or SyntaxKind.ExclusiveOrAssignmentExpression,
            _ => false,
        };
        return isUnnarrowedNode && IsPromotedBuiltinOperator(context, parent, expectedType);
    }

    private static bool IsPromotedBuiltinOperator(CopyCheckContext context, SyntaxNode node, ITypeSymbol closed) =>
        context.Model.GetSymbolInfo(node).Symbol is { } symbol &&
        ConstrainedAccess.IsConstrainable(symbol) &&
        ConstrainedAccess.Find(node, context.Model, symbol) is ITypeParameterSymbol parameter &&
        context.Map.IsMapped(parameter) &&
        context.Map.ResolveConstrained(symbol, parameter) is ConstrainedMember.BuiltinOperator builtin &&
        context.Comparer.SameType(builtin.ClosedType, closed);

    // The call T.Abs(x) that the copy writes as (x), when the node is the argument of one
    private static InvocationExpressionSyntax? ArgumentCallOf(CopyCheckContext context, SyntaxNode? parent) =>
        parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list } &&
        list.Arguments.Count == 1 &&
        context.OriginalNodes.TryGetValue(call, out var callIndex) &&
        context.Plan.Rewrites.TryGetValue(callIndex, out var rewrite) &&
        rewrite is CopyRewrite.ArgumentCall
            ? call
            : null;

    // [WHY] CopyRewrite.ArgumentCall (e.g. T.Abs(x) -> (x)) removes the call boundary, so promotion from an
    // enclosing operator or MathCall flows through the ArgumentCall directly to its argument.
    private static (SyntaxNode Operand, SyntaxNode? Parent) UnwrapParentWithOperand(
        CopyCheckContext context,
        SyntaxNode node)
    {
        var operand = node;
        while (true)
        {
            var parent = operand.Parent;
            if (parent is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
            {
                operand = parent;
                continue;
            }

            if (ArgumentCallOf(context, parent) is { } call)
            {
                operand = call;
                continue;
            }

            return (operand, parent);
        }
    }
}
