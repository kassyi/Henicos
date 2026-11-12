namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes a string as a C++ string literal whose bytes do not depend on the character sets of the C++ compiler: a double
/// quote, a backslash, a control character and every byte of a non-ASCII character (in UTF-8) become a three-digit octal
/// escape. MSVC reads a source file without a byte order mark in the system code page, so a non-ASCII character written as
/// it is would reach the program as other bytes; the host reads the CPU error messages as UTF-8.
/// </summary>
internal static class CppStringLiteral
{
    private const int ReplacementCharacter = 0xFFFD;
    private const string HexDigits = "0123456789abcdef";

    /// <summary>Writes <c>"&lt;value&gt;"</c> with the escapes described on the type.</summary>
    public static void Append(SourceWriter writer, string value)
    {
        writer.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c >= 0x20 && c <= 0x7E && c != '"' && c != '\\')
            {
                writer.Append(c);
                continue;
            }

            int codePoint;
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codePoint = char.ConvertToUtf32(c, value[++i]);
            }
            else
            {
                // A surrogate without its pair has no UTF-8 form; it becomes U+FFFD, as UTF8Encoding writes it
                codePoint = char.IsSurrogate(c) ? ReplacementCharacter : c;
            }

            AppendUtf8Escapes(writer, codePoint);
        }

        writer.Append('"');
    }

    /// <summary>
    /// Writes <c>u"&lt;value&gt;"</c>, a C++ <c>char16_t</c> literal with the UTF-16 code units of
    /// <paramref name="value"/> as C# indexes them: a printable ASCII character other than <c>"</c>, <c>\</c> and
    /// <c>?</c> as it is, any other code unit (a lone surrogate included) as a hexadecimal escape.
    /// </summary>
    /// <remarks>
    /// A hexadecimal escape takes every hexadecimal digit after it, so a digit that follows one starts the next piece
    /// of the literal (<c>u"\x00e9" u"1"</c>), which C++ joins into one literal.
    /// </remarks>
    public static void AppendUtf16(SourceWriter writer, string value)
    {
        writer.Append("u\"");
        var afterEscape = false;
        foreach (var c in value)
        {
            if (c >= 0x20 && c <= 0x7E && c != '"' && c != '\\' && c != '?')
            {
                writer.AppendIf(afterEscape && IsHexDigit(c), "\" u\"");
                writer.Append(c);
                afterEscape = false;
                continue;
            }

            writer.Append("\\x");
            for (var shift = 12; shift >= 0; shift -= 4)
            {
                writer.Append(HexDigits[(c >> shift) & 0xF]);
            }

            afterEscape = true;
        }

        writer.Append('"');
    }

    private static bool IsHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    // The UTF-8 bytes of one code point, each as \ooo (an octal escape ends after three digits, so a following digit is
    // never read into it)
    private static void AppendUtf8Escapes(SourceWriter writer, int codePoint)
    {
        if (codePoint < 0x80)
        {
            AppendOctal(writer, codePoint);
        }
        else if (codePoint < 0x800)
        {
            AppendOctal(writer, 0xC0 | (codePoint >> 6));
            AppendOctal(writer, 0x80 | (codePoint & 0x3F));
        }
        else if (codePoint < 0x10000)
        {
            AppendOctal(writer, 0xE0 | (codePoint >> 12));
            AppendOctal(writer, 0x80 | ((codePoint >> 6) & 0x3F));
            AppendOctal(writer, 0x80 | (codePoint & 0x3F));
        }
        else
        {
            AppendOctal(writer, 0xF0 | (codePoint >> 18));
            AppendOctal(writer, 0x80 | ((codePoint >> 12) & 0x3F));
            AppendOctal(writer, 0x80 | ((codePoint >> 6) & 0x3F));
            AppendOctal(writer, 0x80 | (codePoint & 0x3F));
        }
    }

    private static void AppendOctal(SourceWriter writer, int value)
    {
        writer.Append('\\');
        writer.Append((char)('0' + ((value >> 6) & 7)));
        writer.Append((char)('0' + ((value >> 3) & 7)));
        writer.Append((char)('0' + (value & 7)));
    }
}
