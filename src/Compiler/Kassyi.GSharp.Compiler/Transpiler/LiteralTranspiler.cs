using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles a C# literal to the C++ literal of the same type and value.</summary>
internal static class LiteralTranspiler
{
    /// <summary>
    /// Writes <c>null</c> converted to an array or span as the empty view of its C++ type (<c>GSharpArray&lt;int&gt;()</c>,
    /// whose <c>data</c> is <c>nullptr</c>): the view has no constructor from <c>nullptr</c>, so <c>int[] x = null</c> and
    /// <c>c ? x : null</c> would not compile. False for any other literal, which <see cref="Transpile"/> writes.
    /// </summary>
    public static bool TryAppendArrayNull(LiteralExpressionSyntax node, ref TranspileContext context)
    {
        if (!node.IsKind(SyntaxKind.NullLiteralExpression) ||
            context.Query.SemanticModel.GetConvertedTypeSymbol(node) is not { } type ||
            !type.IsArrayLikeType())
        {
            return false;
        }

        AppendEmptyView(type, ref context);
        return true;
    }

    /// <summary>
    /// Writes the empty view of the array-like <paramref name="type"/>, <c>GSharpArray&lt;int&gt;()</c>: its <c>data</c>
    /// is <c>nullptr</c>, which is how a null array is represented.
    /// </summary>
    public static void AppendEmptyView(ITypeSymbol type, ref TranspileContext context)
    {
        var typeContext = DialectTranspilerExtensions.CreateTypeContext(ref context);
        CppLiteralWriter.AppendEmptyView(in typeContext, type);
    }

    /// <summary>Writes <paramref name="node"/> as a C++ literal.</summary>
    public static void Transpile(LiteralExpressionSyntax node, SourceWriter writer)
    {
        switch (node.Kind())
        {
            case SyntaxKind.DefaultLiteralExpression:
                writer.Append("{}");
                return;
            case SyntaxKind.NullLiteralExpression:
                writer.Append("nullptr");
                return;
            case SyntaxKind.NumericLiteralExpression:
                AppendNumeric(node.Token, writer);
                return;
            // A C# char is a UTF-16 code unit; the C++ character literal is a char in the source's encoding ('é' is
            // two bytes).
            case SyntaxKind.CharacterLiteralExpression when node.Token.Value is char character:
                writer.Append(ConstexprLiteral.FormatCppChar(character));
                return;
            // A string with a non-ASCII character (a source file name in a CPU error message, a printf format) is
            // written with octal escapes, so its UTF-8 bytes do not depend on the character sets of the C++ compiler;
            // any other string is written as it is, its C# escapes being C++ escapes as well
            case SyntaxKind.StringLiteralExpression when HasNonAscii(node.Token.ValueText):
                CppStringLiteral.Append(writer, node.Token.ValueText);
                return;
            default:
                writer.Append(node.Token.Text);
                return;
        }
    }

    /// <summary>
    /// Writes the C# numeric literal <paramref name="text"/>, whose value has the type <paramref name="literalType"/>
    /// (<see cref="GetLiteralType"/>), as the C++ literal of that type.
    /// </summary>
    internal static void AppendNumeric(string text, SpecialType literalType, SourceWriter writer) =>
        CppLiteralWriter.AppendNumeric(text, literalType, writer);

    /// <summary>
    /// The type of the value of a numeric literal token: Int64, UInt64, UInt32, Single, Double or Int32; None for any
    /// other.
    /// </summary>
    internal static SpecialType GetLiteralType(object? value) =>
        CppLiteralWriter.GetLiteralType(value);

    private static void AppendNumeric(SyntaxToken token, SourceWriter writer) =>
        AppendNumeric(token.Text, GetLiteralType(token.Value), writer);

    private static bool HasNonAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 0x7F)
            {
                return true;
            }
        }

        return false;
    }
}
