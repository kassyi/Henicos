namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Escapes text for an XML documentation comment of generated C#. Shared by the source generators and the build tool
/// (<c>Kassyi.GSharp.Compiler.Generators</c>), which links this file.
/// </summary>
public static class XmlDocEscaping
{
    private static readonly char[] s_special = ['&', '<', '>'];
    private static readonly char[] s_specialOrLineBreak = ['&', '<', '>', '\n'];

    /// <summary>
    /// Escapes <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>, and returns <paramref name="text"/> itself when it holds none
    /// of them.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <param name="lineBreak">
    /// When not <see langword="null"/>, written in place of each <c>\n</c> (for example <c>"\n/// "</c> to continue the
    /// comment), and every <c>\r</c> is dropped; when <see langword="null"/>, line breaks are copied as they are.
    /// </param>
    public static string Escape(string text, string? lineBreak = null)
    {
        if (text.IndexOfAny(lineBreak is null ? s_special : s_specialOrLineBreak) < 0)
        {
            return text;
        }

        using var escaped = new SourceWriter();
        foreach (var c in text)
        {
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
                case '\n' when lineBreak is not null:
                    escaped.Append(lineBreak);
                    break;
                case '\r' when lineBreak is not null:
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }
}
