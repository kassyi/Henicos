namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The nodes a copy writes anew from a plan (a constant, false, a name, a Math call, a narrowed operator): the form and
/// the value of the copy match what the plan says. One method per kind of rewrite, one line of the dispatcher each.
/// </summary>
internal static class CopyWrittenForms
{
    /// <summary>The reason a node written as <paramref name="rewrite"/> differs from the plan, or null.</summary>
    public static string? Compare(
        CopyCheckContext context,
        CopyRewrite rewrite,
        SyntaxNode original,
        SyntaxNode copied) =>
        rewrite switch
        {
            CopyRewrite.IdentityConstant identity => CompareIdentity(context, identity, copied),
            CopyRewrite.StaticMember member => CompareStaticMember(context, member, copied),
            CopyRewrite.NameOf nameOf => CompareNameOf(context, nameOf, copied),
            CopyRewrite.FalseCall => CompareFalse(context, copied),
            CopyRewrite.NarrowedOperator narrowed => CompareNarrowedOperator(context, narrowed, original, copied),
            CopyRewrite.NarrowedShift shift => CompareNarrowedShift(context, shift, original, copied),
            CopyRewrite.TypedNegation negation => CompareTypedNegation(context, negation, copied),
            CopyRewrite.NarrowedNegation negation => CompareNarrowedNegation(context, negation, copied),
            CopyRewrite.NarrowedCompoundDivision division => CompareNarrowedCompoundDivision(context, division, copied),
            CopyRewrite.NarrowedCompoundShift shift => CompareNarrowedCompoundShift(context, shift, copied),
            _ => null,
        };

    // [WHY] Every planned rewrite must be verified against the copy's syntax and bound symbols so an un-narrowed
    // compound assignment, wrong Math/static member, or wrong operator cannot pass just because the plan has it.
    public static string? CompareMathCall(CopyCheckContext context, CopyRewrite.MathCall math, SyntaxNode copied)
    {
        var node = CopyCheckContext.UnwrapNode(copied);
        if (math.ResultType is not null)
        {
            if (node is not CastExpressionSyntax cast)
            {
                return "the copy does not cast the Math call";
            }

            node = CopyCheckContext.UnwrapNode(cast.Expression);
        }

        if (node is not InvocationExpressionSyntax invocation ||
            context.CopyModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { IsStatic: true } callee ||
            callee.Name != math.Name || !context.IsSystemMath(callee.ContainingType))
        {
            return $"the copy does not call 'System.Math.{math.Name}'";
        }

        return null;
    }

    private static string? CompareIdentity(
        CopyCheckContext context,
        CopyRewrite.IdentityConstant identity,
        SyntaxNode copied) =>
        CopyIdentityForms.IsIdentityConstant(context, identity, copied)
            ? null
            : "the copy does not give the identity of the type";

    private static string? CompareStaticMember(
        CopyCheckContext context,
        CopyRewrite.StaticMember member,
        SyntaxNode copied) =>
        CopyIdentityForms.IsStaticMember(context, member, copied)
            ? null
            : $"the copy does not read '{member.Type.Name}.{member.Name}'";

    private static string? CompareNameOf(CopyCheckContext context, CopyRewrite.NameOf nameOf, SyntaxNode copied) =>
        Equals(context.CopyModel.GetConstantValue(copied).Value, nameOf.Text)
            ? null
            : "the copy does not give the name the declaration gives";

    private static string? CompareFalse(CopyCheckContext context, SyntaxNode copied) =>
        Equals(context.CopyModel.GetConstantValue(copied).Value, false) ? null : "the copy does not give false";

    private static string? CompareNarrowedOperator(
        CopyCheckContext context,
        CopyRewrite.NarrowedOperator narrowed,
        SyntaxNode original,
        SyntaxNode copied) =>
        IsNarrowedOperator(context, narrowed, original, copied) ? null : "the copy does not narrow the operator";

    private static string? CompareNarrowedShift(
        CopyCheckContext context,
        CopyRewrite.NarrowedShift shift,
        SyntaxNode original,
        SyntaxNode copied) =>
        IsNarrowedShift(context, shift, original, copied) ? null : "the copy does not narrow the shift";

    private static string? CompareTypedNegation(
        CopyCheckContext context,
        CopyRewrite.TypedNegation negation,
        SyntaxNode copied) =>
        IsTypedNegation(context, negation, copied) ? null : "the copy does not negate in the type";

    private static string? CompareNarrowedNegation(
        CopyCheckContext context,
        CopyRewrite.NarrowedNegation negation,
        SyntaxNode copied) =>
        IsNarrowedNegation(context, negation, copied) ? null : "the copy does not narrow the negation";

    private static string? CompareNarrowedCompoundDivision(
        CopyCheckContext context,
        CopyRewrite.NarrowedCompoundDivision division,
        SyntaxNode copied) =>
        IsNarrowedCompoundDivision(context, division, copied) ? null : "the copy does not narrow the operator";

    private static string? CompareNarrowedCompoundShift(
        CopyCheckContext context,
        CopyRewrite.NarrowedCompoundShift shift,
        SyntaxNode copied) =>
        IsNarrowedCompoundShift(context, shift, copied) ? null : "the copy does not narrow the shift";

    private static bool IsNarrowedOperator(
        CopyCheckContext context,
        CopyRewrite.NarrowedOperator narrowed,
        SyntaxNode original,
        SyntaxNode copied) =>
        UnwrapNarrowing(context, copied, narrowed.Type, narrowed.Narrowing) is { } inner &&
        inner.Kind() == original.Kind() &&
        CopyBindings.CompareSymbols(context, original, inner) is null;

    private static bool IsNarrowedShift(
        CopyCheckContext context,
        CopyRewrite.NarrowedShift shift,
        SyntaxNode original,
        SyntaxNode copied) =>
        UnwrapNarrowing(context, copied, shift.Type, shift.Narrowing) is { } inner &&
        IsMaskedShift(context, inner, original.Kind(), shift.CountMask, shift.LeftMask);

    private static bool IsTypedNegation(
        CopyCheckContext context,
        CopyRewrite.TypedNegation negation,
        SyntaxNode copied) =>
        CopyCheckContext.UnwrapNode(copied) is
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.SubtractExpression } sub &&
        IsZeroConstantOrCast(context, sub.Left) &&
        context.Comparer.SameType(negation.Type, context.CopyModel.GetTypeInfo(sub).Type);

    private static bool IsNarrowedNegation(
        CopyCheckContext context,
        CopyRewrite.NarrowedNegation negation,
        SyntaxNode copied) =>
        UnwrapNarrowing(context, copied, negation.Type, negation.Narrowing) is
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.SubtractExpression } sub &&
        IsZeroConstantOrCast(context, sub.Left);

    private static bool IsNarrowedCompoundDivision(
        CopyCheckContext context,
        CopyRewrite.NarrowedCompoundDivision division,
        SyntaxNode copied) =>
        CopyCheckContext.UnwrapNode(copied) is AssignmentExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
        } assignment &&
        UnwrapNarrowing(context, assignment.Right, division.Type, division.Narrowing) is
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.DivideExpression };

    private static bool IsNarrowedCompoundShift(
        CopyCheckContext context,
        CopyRewrite.NarrowedCompoundShift shift,
        SyntaxNode copied) =>
        CopyCheckContext.UnwrapNode(copied) is AssignmentExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
        } assignment &&
        UnwrapNarrowing(context, assignment.Right, shift.Type, shift.Narrowing) is { } inner &&
        IsMaskedShift(context, inner, shift.Operator, shift.CountMask, shift.LeftMask);

    private static ExpressionSyntax? UnwrapNarrowing(
        CopyCheckContext context,
        SyntaxNode node,
        ITypeSymbol type,
        Narrowing narrowing)
    {
        if (CopyCheckContext.UnwrapNode(node) is not CastExpressionSyntax cast ||
            !context.Comparer.SameType(type, context.CopyModel.GetTypeInfo(cast).Type))
        {
            return null;
        }

        var inRange = CopyCheckContext.UnwrapNode(cast.Expression);
        return narrowing switch
        {
            Narrowing.Cast => inRange as ExpressionSyntax,
            Narrowing.LowBits lowBits => UnwrapBitwiseMask(context, inRange, lowBits.Mask),
            Narrowing.SignExtended extended => UnwrapSignExtension(context, inRange, extended.Shift),
            _ => null,
        };
    }

    private static ExpressionSyntax? UnwrapSignExtension(CopyCheckContext context, SyntaxNode node, int shift) =>
        node is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.RightShiftExpression } rightShift &&
        context.CopyModel.GetConstantValue(rightShift.Right) is { HasValue: true } rightConst &&
        CopyIdentityForms.IsNumber(rightConst.Value, shift) &&
        CopyCheckContext.UnwrapNode(rightShift.Left) is
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LeftShiftExpression } leftShift &&
        context.CopyModel.GetConstantValue(leftShift.Right) is { HasValue: true } leftConst &&
        CopyIdentityForms.IsNumber(leftConst.Value, shift)
            ? CopyCheckContext.UnwrapNode(leftShift.Left) as ExpressionSyntax
            : null;

    private static ExpressionSyntax? UnwrapBitwiseMask(CopyCheckContext context, SyntaxNode node, int mask) =>
        node is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.BitwiseAndExpression } andExpr &&
        context.CopyModel.GetConstantValue(andExpr.Right) is { HasValue: true } constant &&
        CopyIdentityForms.IsNumber(constant.Value, mask)
            ? CopyCheckContext.UnwrapNode(andExpr.Left) as ExpressionSyntax
            : null;

    private static bool IsMaskedShift(
        CopyCheckContext context,
        SyntaxNode node,
        SyntaxKind operatorKind,
        int countMask,
        int? leftMask) =>
        node is BinaryExpressionSyntax binary &&
        binary.Kind() == operatorKind &&
        UnwrapBitwiseMask(context, CopyCheckContext.UnwrapNode(binary.Right), countMask) is not null &&
        (leftMask is not { } mask ||
         UnwrapBitwiseMask(context, CopyCheckContext.UnwrapNode(binary.Left), mask) is not null);

    private static bool IsZeroConstantOrCast(CopyCheckContext context, ExpressionSyntax expression)
    {
        var constant = context.CopyModel.GetConstantValue(expression);
        if (constant.HasValue)
        {
            return CopyIdentityForms.IsNumber(constant.Value, 0);
        }

        return CopyCheckContext.UnwrapNode(expression) is CastExpressionSyntax cast &&
               context.CopyModel.GetConstantValue(cast.Expression) is { HasValue: true } inner &&
               CopyIdentityForms.IsNumber(inner.Value, 0);
    }
}
