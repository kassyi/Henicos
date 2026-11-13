namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Escapes text for an XML documentation comment of generated C#. Shared by the source generators and the build tool
/// (<c>Kassyi.GSharp.Compiler.Generators</c>), which links this file.
/// </summary>
public static class XmlDocEscaping
{
    private static readonly char[] s_special = ['&', '<', '>'];
    private static readonly char[] s_specialOrLineBreak = ['&', '<', '>', '\r', '\n', '\u0085', '\u2028', '\u2029'];

    /// <summary>
    /// The characters that end a line of C# source, and with it a <c>///</c> comment: <c>\r</c>, <c>\n</c>, U+0085,
    /// U+2028 and U+2029.
    /// </summary>
    public static readonly char[] LineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029'];

    /// <summary>
    /// Escapes <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>, and returns <paramref name="text"/> itself when it holds none
    /// of them.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <param name="lineBreak">
    /// When not <see langword="null"/>, written in place of each line break that C# source knows (for example
    /// <c>"\n/// "</c> to continue the comment): <c>\r\n</c>, <c>\r</c>, <c>\n</c>, U+0085, U+2028 and U+2029; when
    /// <see langword="null"/>, line breaks are copied as they are.
    /// </param>
    public static string Escape(string text, string? lineBreak = null)
    {
        if (text.IndexOfAny(lineBreak is null ? s_special : s_specialOrLineBreak) < 0)
        {
            return text;
        }

        using var escaped = new SourceWriter();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                case '\r' when lineBreak is not null && i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\r' or '\n' or '\u0085' or '\u2028' or '\u2029' when lineBreak is not null:
                    escaped.Append(lineBreak);
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }
}
