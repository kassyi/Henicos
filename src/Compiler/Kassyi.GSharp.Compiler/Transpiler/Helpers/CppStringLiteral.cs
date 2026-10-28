namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes a string as a C++ string literal whose bytes do not depend on the character sets of the C++ compiler: a double
/// quote, a backslash, a control character and every byte of a non-ASCII character (in UTF-8) become a three-digit octal
/// escape. MSVC reads a source file without a byte order mark in the system code page, so a non-ASCII character written as
/// it is would reach the program as other bytes; the host reads the CPU error messages as UTF-8.
/// </summary>
internal static class CppStringLiteral
{
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

            var codePoint = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                ? char.ConvertToUtf32(c, value[++i])
                : c;
            AppendUtf8Escapes(writer, codePoint);
        }

        writer.Append('"');
    }

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
