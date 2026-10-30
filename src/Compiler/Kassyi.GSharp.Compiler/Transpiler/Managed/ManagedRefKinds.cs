namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The C# modifiers of a <see cref="RefKind"/> in the code the managed path writes: on an argument (<c>ref</c>,
/// <c>out</c>, <c>in</c>) and on a parameter (<c>ref</c>, <c>out</c>, <c>in</c>, <c>ref readonly</c>).
/// </summary>
internal static class ManagedRefKinds
{
    /// <summary>
    /// The keyword of an argument passed to a parameter of <paramref name="refKind"/>, or <see cref="SyntaxKind.None"/>
    /// for a value parameter. A <c>ref readonly</c> parameter takes <c>in</c>.
    /// </summary>
    public static SyntaxKind ArgumentKeyword(RefKind refKind) => refKind switch
    {
        RefKind.Ref => SyntaxKind.RefKeyword,
        RefKind.Out => SyntaxKind.OutKeyword,
        RefKind.In or RefKind.RefReadOnlyParameter => SyntaxKind.InKeyword,
        _ => SyntaxKind.None
    };

    /// <summary><see cref="ArgumentKeyword"/> as text with a space after it (<c>"ref "</c>), or empty.</summary>
    public static string ArgumentModifier(RefKind refKind) => ArgumentKeyword(refKind) switch
    {
        SyntaxKind.RefKeyword => "ref ",
        SyntaxKind.OutKeyword => "out ",
        SyntaxKind.InKeyword => "in ",
        _ => string.Empty
    };

    /// <summary>The modifier of a parameter of <paramref name="refKind"/> with a space after it, or empty.</summary>
    public static string ParameterModifier(RefKind refKind) => refKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => string.Empty
    };
}
