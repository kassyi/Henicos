namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes the <c>Length</c> of a constant string (<c>nameof(T).Length</c>, <c>"abc".Length</c>, the <c>Length</c> of a
/// <c>const string</c>) as the value C# gives it: the string itself has no device form.
/// </summary>
/// <remarks>
/// The <c>Length</c> of a constant that is <see langword="null"/> throws <see cref="NullReferenceException"/> in C#,
/// which is known at compile time; it is a failure (GSH0003, as the semantics charter treats certain undefined
/// behavior), never the length of a null pointer.
/// </remarks>
internal static class ConstantStringLength
{
    /// <summary>The GSH0003 text for the <c>Length</c> of a constant string that is <see langword="null"/>.</summary>
    public const string NullConstant = "Length of a null constant string (it throws NullReferenceException)";

    /// <summary>
    /// Writes <paramref name="node"/>, a member access of <paramref name="symbol"/>, when it is the <c>Length</c> of a
    /// constant string: its value, or the placeholder with GSH0003 for a <see langword="null"/> constant.
    /// </summary>
    /// <returns>Whether <paramref name="node"/> is such a <c>Length</c> and was written.</returns>
    public static bool TryAppend(MemberAccessExpressionSyntax node, ISymbol? symbol, ref TranspileContext context)
    {
        if (symbol is not IPropertySymbol
            {
                Name: nameof(string.Length),
                ContainingType.SpecialType: SpecialType.System_String
            } length ||
            context.Query.SemanticModel.GetSafeConstantValue(node.Expression) is not { HasValue: true } constant)
        {
            return false;
        }

        if (constant.Value is not string text)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, NullConstant);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return true;
        }

        ConstantAtUse.Append(node, length.Type, text.Length, ref context);
        return true;
    }
}
