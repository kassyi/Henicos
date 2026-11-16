namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles a C# literal to the C++ literal of the same type and value.</summary>
internal static class LiteralTranspiler
{
    // A floating-point literal without one of these is an integer in C++ and needs ".0"
    private static readonly char[] s_fractionMarks = ['.', 'e', 'E'];

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
        context.Emitter.Dialect.AppendType(ref context, type);
        context.Emitter.Writer.Append("()");
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
    internal static void AppendNumeric(string text, SpecialType literalType, SourceWriter writer)
    {
        switch (literalType)
        {
            // C# long/ulong are always 64-bit, but C++ 'L'/'UL' are 32-bit under LLP64 (MSVC, Windows nvcc).
            case SpecialType.System_Int64 or SpecialType.System_UInt64:
                AppendInt64Literal(text, literalType == SpecialType.System_UInt64, writer);
                return;
            // An unsuffixed decimal C# literal from 2^31 to 2^32 - 1 is uint, but C++ gives a decimal literal the first
            // of int, long and long long that holds it, a signed 64-bit type under LP64 (g++), so it carries the 'u' of
            // its C# type. A hexadecimal or binary literal of that value is unsigned int in C++ as well.
            case SpecialType.System_UInt32 when IsUnsuffixedDecimal(text):
                AppendWithoutSeparators(text, text.Length, writer);
                writer.Append('u');
                return;
            // C# accepts "2f" and "2d", but C++ treats "2" as an integer literal (so "f" is an unknown suffix) and has
            // no "d" suffix.
            case SpecialType.System_Single or SpecialType.System_Double:
                AppendFloatingPointLiteral(text, literalType == SpecialType.System_Single, writer);
                return;
            // C++ rejects '_' inside numbers (and "0x'FF"), so digit separators are dropped.
            default:
                AppendWithoutSeparators(text, text.Length, writer);
                return;
        }
    }

    /// <summary>
    /// The type of the value of a numeric literal token: Int64, UInt64, UInt32, Single, Double or Int32; None for any
    /// other.
    /// </summary>
    internal static SpecialType GetLiteralType(object? value) =>
        value switch
        {
            long => SpecialType.System_Int64,
            ulong => SpecialType.System_UInt64,
            uint => SpecialType.System_UInt32,
            float => SpecialType.System_Single,
            double => SpecialType.System_Double,
            int => SpecialType.System_Int32,
            _ => SpecialType.None
        };

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

    private static void AppendWithoutSeparators(string text, int end, SourceWriter writer)
    {
        for (var i = 0; i < end; i++)
        {
            if (text[i] != '_')
            {
                writer.Append(text[i]);
            }
        }
    }

    private static void AppendFloatingPointLiteral(string text, bool isSingle, SourceWriter writer)
    {
        var end = TrimSuffix(text, "fFdD");
        var hasFraction = text.IndexOfAny(s_fractionMarks, 0, end) >= 0;

        AppendWithoutSeparators(text, end, writer);
        writer.AppendIf(!hasFraction, ".0");
        writer.AppendIf(isSingle, "f");
    }

    // A decimal literal (not 0x / 0b) without the u or U suffix
    private static bool IsUnsuffixedDecimal(string text) =>
        !(text.Length > 1 && text[0] == '0' && text[1] is 'x' or 'X' or 'b' or 'B') &&
        text[text.Length - 1] is not ('u' or 'U');

    private static void AppendInt64Literal(string text, bool isUnsigned, SourceWriter writer)
    {
        AppendWithoutSeparators(text, TrimSuffix(text, "uUlL"), writer);
        writer.Append(isUnsigned ? "ull" : "ll");
    }

    // The length of text without its trailing suffix letters
    private static int TrimSuffix(string text, string suffixLetters)
    {
        var end = text.Length;
        while (end > 0 && suffixLetters.IndexOf(text[end - 1]) >= 0)
        {
            end--;
        }

        return end;
    }
}
