namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Symbol-level predicates for built-in methods handled by dedicated invocation lowering/emission rules rather than
/// dialect intrinsic mapping.
/// </summary>
internal static class InvocationRuleClaims
{
    /// <summary>Whether <paramref name="symbol"/> is one of the signed integer overloads of <c>Math.Abs</c>.</summary>
    public static bool ClaimsMathAbs(IMethodSymbol symbol) =>
        symbol is
        {
            Name: "Abs",
            Parameters: [{ Type.SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64 or
                SpecialType.System_Int16 or SpecialType.System_SByte or SpecialType.System_IntPtr }]
        } &&
        symbol.ContainingType is { Name: "Math", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    /// <summary>
    /// Whether a call of <paramref name="symbol"/> (with <paramref name="isMemberAccessCall"/> indicating member-access
    /// syntax) is a call of <c>GetValueOrDefault</c> on <c>Nullable&lt;T&gt;</c>.
    /// </summary>
    public static bool ClaimsNullableGetValueOrDefault(IMethodSymbol symbol, bool isMemberAccessCall) =>
        symbol is { Name: "GetValueOrDefault", ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } &&
        isMemberAccessCall;
}
