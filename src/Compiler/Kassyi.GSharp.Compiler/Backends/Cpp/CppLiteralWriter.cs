namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Context-free C++ spelling helpers for numeric literals and empty array-view expressions.
/// </summary>
internal static class CppLiteralWriter
{
    // A floating-point literal without one of these is an integer in C++ and needs ".0"
    private static readonly char[] s_fractionMarks = ['.', 'e', 'E'];

    /// <summary>
    /// Writes the empty view of the array-like <paramref name="type"/>, <c>GSharpArray&lt;int&gt;()</c>: its <c>data</c>
    /// is <c>nullptr</c>, which is how a null array is represented.
    /// </summary>
    public static void AppendEmptyView<TServices>(in CppTypeContext<TServices> context, ITypeSymbol type)
        where TServices : struct, ICppTypeServices
    {
        CppTypeWriter.AppendType(in context, type);
        context.Writer.Append("()");
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
