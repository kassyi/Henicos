namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The forms in which a copy writes the identity of a type or a static member of it: as a constant, or, where C# would
/// fold an operator on two constants, as the opaque <c>(T)Math.Max(c, c)</c> that it cannot fold.
/// </summary>
internal static class CopyIdentityForms
{
    // [WHY] A constant identity matches its numeric value (0m == 0.0m in C# decimal); when marked opaque to
    // prevent constant folding of / 0 or checked overflow, it matches the emitter's ((T)Math.Max(c, c)) form.
    public static bool IsIdentityConstant(
        CopyCheckContext context,
        CopyRewrite.IdentityConstant identity,
        SyntaxNode copied)
    {
        var constant = context.CopyModel.GetConstantValue(copied);
        if (constant.HasValue)
        {
            return IsNumber(constant.Value, identity.Value);
        }

        return identity.Opaque && TryUnwrapOpaqueMax(context, copied) is var (first, second) &&
               IsTypedNumber(context, first, identity.Type, identity.Value) &&
               IsTypedNumber(context, second, identity.Type, identity.Value);
    }

    public static bool IsStaticMember(CopyCheckContext context, CopyRewrite.StaticMember member, SyntaxNode copied)
    {
        if (member.Opaque)
        {
            return TryUnwrapOpaqueMax(context, copied) is var (first, second) &&
                   IsStaticMemberCast(context, first, member.Type, member.Name) &&
                   IsStaticMemberCast(context, second, member.Type, member.Name);
        }

        return IsStaticMemberCast(context, copied, member.Type, member.Name);
    }

    /// <summary>Whether the constant is the integer <paramref name="expected"/>; floating point by its bits.</summary>
    public static bool IsNumber(object? value, int expected) =>
        value switch
        {
            int i => i == expected,
            uint u => expected >= 0 && u == (uint)expected,
            long l => l == expected,
            ulong ul => expected >= 0 && ul == (ulong)expected,
            short s => s == expected,
            ushort us => expected >= 0 && us == (ushort)expected,
            sbyte sb => sb == expected,
            byte b => expected >= 0 && b == (byte)expected,
            char c => expected >= 0 && c == (char)expected,
            float f => SameBits(f, expected),
            double d => SameBits(d, expected),
            decimal m => m == expected,
            _ => false,
        };

    // [WHY] -0.0 == 0 in C#, but T.Zero is +0.0 and the two differ for 1 / x and Math.CopySign, so a floating-point
    // identity matches by its bits (float to double is exact, so one comparison serves both)
    private static bool SameBits(double value, int expected) =>
        BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(expected);

    private static bool IsStaticMemberCast(
        CopyCheckContext context,
        SyntaxNode node,
        ITypeSymbol type,
        string name)
    {
        if (CopyCheckContext.UnwrapNode(node) is not CastExpressionSyntax cast ||
            !context.Comparer.SameType(type, context.CopyModel.GetTypeInfo(cast).Type))
        {
            return false;
        }

        return CopyCheckContext.UnwrapNode(cast.Expression) is MemberAccessExpressionSyntax access &&
               context.CopyModel.GetSymbolInfo(access).Symbol is { IsStatic: true } symbol &&
               symbol.DeclaredAccessibility == Accessibility.Public &&
               symbol.Name == name &&
               context.Comparer.SameType(type, symbol.ContainingType);
    }

    // The two arguments of the (T)Math.Max(first, second) that the copy writes for an opaque constant, or null
    private static (ExpressionSyntax First, ExpressionSyntax Second)? TryUnwrapOpaqueMax(
        CopyCheckContext context,
        SyntaxNode copied)
    {
        if (CopyCheckContext.UnwrapNode(copied) is not CastExpressionSyntax cast ||
            CopyCheckContext.UnwrapNode(cast.Expression) is not InvocationExpressionSyntax invocation ||
            invocation.ArgumentList.Arguments is not [var first, var second] ||
            !first.RefKindKeyword.IsKind(SyntaxKind.None) ||
            !second.RefKindKeyword.IsKind(SyntaxKind.None))
        {
            return null;
        }

        if (context.CopyModel.GetSymbolInfo(invocation).Symbol is not
            IMethodSymbol { Name: "Max", IsStatic: true } max || !context.IsSystemMath(max.ContainingType))
        {
            return null;
        }

        return (first.Expression, second.Expression);
    }

    private static bool IsTypedNumber(
        CopyCheckContext context,
        ExpressionSyntax expression,
        ITypeSymbol expectedType,
        int expectedValue)
    {
        var constant = context.CopyModel.GetConstantValue(expression);
        return constant.HasValue && IsNumber(constant.Value, expectedValue) &&
               context.Comparer.SameType(expectedType, context.CopyModel.GetTypeInfo(expression).Type);
    }
}
