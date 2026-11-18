namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Compares one node of the copy with the node of the declaration it came from, in steps: the type, the converted
/// type (with the promotions C# allows), then the planned rewrite or, where there is none, the symbol, the conversion
/// and the constant value.
/// </summary>
internal static class CopyNodeComparer
{
    /// <summary>The reason the copied node differs, or null.</summary>
    public static string? Compare(CopyCheckContext context, int index, SyntaxNode original, SyntaxNode copied)
    {
        var originalType = context.Model.GetTypeInfo(original);
        var copyType = context.CopyModel.GetTypeInfo(copied);
        var expectedType = context.Substitute(originalType.Type);
        if (CompareType(context, expectedType, originalType, copyType) is { } typeReason)
        {
            return typeReason;
        }

        if (CompareConvertedType(context, original, copied, expectedType, originalType, copyType, out var promoted) is
            { } convertedReason)
        {
            return convertedReason;
        }

        return CompareByPlan(context, index, original, copied, promoted);
    }

    private static string? CompareType(
        CopyCheckContext context,
        ITypeSymbol? expectedType,
        TypeInfo originalType,
        TypeInfo copyType) =>
        context.Comparer.SameType(expectedType, copyType.Type)
            ? null
            : $"the copy has the type '{copyType.Type?.Name}' where the declaration has '{originalType.Type?.Name}'";

    // Whether the type the copy converts to is the one of the declaration, or the promotion C# makes in its place
    private static string? CompareConvertedType(
        CopyCheckContext context,
        SyntaxNode original,
        SyntaxNode copied,
        ITypeSymbol? expectedType,
        TypeInfo originalType,
        TypeInfo copyType,
        out bool promoted)
    {
        var expectedConverted = context.Substitute(originalType.ConvertedType);
        promoted = false;
        if (context.Comparer.SameType(expectedConverted, copyType.ConvertedType))
        {
            return null;
        }

        promoted = CopyPromotion.IsAllowedPromotion(context, original, copied, expectedType, expectedConverted,
            copyType.ConvertedType);
        return promoted
            ? null
            : $"the copy converts to '{copyType.ConvertedType?.Name}' " +
              $"where the declaration converts to '{originalType.ConvertedType?.Name}'";
    }

    // The planned rewrite of the node decides what the copy must show; a node the plan leaves alone binds, converts
    // and equals what the declaration does
    private static string? CompareByPlan(
        CopyCheckContext context,
        int index,
        SyntaxNode original,
        SyntaxNode copied,
        bool promoted)
    {
        if (context.Plan.Rewrites.TryGetValue(index, out var rewrite))
        {
            if (rewrite is not (CopyRewrite.TypeArgument or CopyRewrite.ExplicitTypeArguments or
                CopyRewrite.StaticExtensionCall))
            {
                return CompareRewrite(context, rewrite, original, copied, promoted);
            }
        }
        else if (CopyPromotion.IsMissingNarrowing(context, original))
        {
            return original.IsKind(SyntaxKind.DivideAssignmentExpression)
                ? "the copy does not narrow the operator"
                : "the copy does not narrow the shift";
        }

        return CopyBindings.CompareSymbols(context, original, copied) ??
               (promoted ? null : CopyBindings.CompareConversions(context, original, copied)) ??
               CopyBindings.CompareConstants(context, original, copied);
    }

    private static string? CompareRewrite(
        CopyCheckContext context,
        CopyRewrite rewrite,
        SyntaxNode original,
        SyntaxNode copied,
        bool promoted)
    {
        switch (rewrite)
        {
            case CopyRewrite.ConstrainedCall or CopyRewrite.ArgumentCall:
                return promoted ? null : CopyBindings.CompareConversions(context, original, copied);
            case CopyRewrite.MathCall math:
                return CopyWrittenForms.CompareMathCall(context, math, copied) ??
                       (promoted ? null : CopyBindings.CompareConversions(context, original, copied));
            default:
                return CopyWrittenForms.Compare(context, rewrite, original, copied);
        }
    }
}
