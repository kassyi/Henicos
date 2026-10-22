namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// XML documentation output shared by <see cref="NativeIntrinsicEmitter"/> and <c>NativeBindingEmitter</c>.
/// </summary>
internal static class NativeApiDocumentation
{
    public static void EmitDocumentation(SourceWriter writer, ApiMethodDefinition m)
    {
        if (!string.IsNullOrEmpty(m.Summary))
        {
            writer.Line($"/// <summary>{EscapeXml(m.Summary ?? string.Empty)}</summary>");
        }

        foreach (var p in m.Parameters)
        {
            if (!string.IsNullOrEmpty(p.Description))
            {
                writer.Line($"/// <param name=\"{p.Name}\">{EscapeXml(p.Description ?? string.Empty)}</param>");
            }
        }

        if (!string.IsNullOrEmpty(m.ReturnDescription))
        {
            writer.Line($"/// <returns>{EscapeXml(m.ReturnDescription ?? string.Empty)}</returns>");
        }
    }

    /// <summary>
    /// Escapes XML special characters and keeps multi-line text inside the <c>///</c> comment.
    /// </summary>
    private static string EscapeXml(string text)
    {
        if (text.IndexOf('&') < 0 && text.IndexOf('<') < 0 && text.IndexOf('>') < 0 && text.IndexOf('\n') < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '\n':
                    sb.Append("\n/// ");
                    break;
                case '\r':
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
